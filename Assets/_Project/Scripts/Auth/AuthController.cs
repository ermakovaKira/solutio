using System;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Mono.Data.Sqlite;
using Solutio.Database;
using UnityEngine;

namespace Solutio.Auth
{
    /// <summary>
    /// Центральный синглтон управления аутентификацией, SMTP-верификацией
    /// и безопасной регистрацией пользователей в базе данных SQLite.
    /// Потокобезопасная архитектура для Unity 6.
    /// </summary>
    public sealed class AuthController : MonoBehaviour
    {
        public static AuthController Instance { get; private set; }

        [Header("SMTP Server Configuration")]
        [SerializeField] private string smtpHost = "smtp.mail.ru";
        [SerializeField] private int smtpPort = 2525;
        [SerializeField] private bool enableSsl = true;
        [SerializeField] private string senderEmail = "solutio.auth@mail.ru";
        [SerializeField] private string senderPassword = "";
        [SerializeField] private string senderDisplayName = "Solutio: Dark Fantasy";

        [Header("Security & Timeout Settings")]
        [SerializeField] private float codeLifetimeSeconds = 300f;
        [SerializeField] private bool mockMode = false;

        // Временный буфер верификации
        private string _activeTargetEmail = string.Empty;
        private string _activeGeneratedCode = string.Empty;
        private DateTime _codeExpirationTimestamp;
        private bool _isCurrentlySending;

        // Данные кандидата на регистрацию (пароль хранится ТОЛЬКО в виде хэша SHA-256)
        private string _pendingLogin = string.Empty;
        private string _pendingEmail = string.Empty;
        private string _pendingPasswordHash = string.Empty;
        private bool _isRegistrationPending = false;

        // События жизненного цикла (гарантированно вызываются в Main Thread)
        public event Action<string> OnCodeSentSuccessfully;
        public event Action<string> OnCodeSendFailed;
        public event Action<string> OnEmailVerified;
        public event Action<int, string> OnUserRegisteredSuccessfully;
        public event Action<string> OnRegistrationDatabaseError;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
        }

        #region Security & Code Generation

        /// <summary>
        /// Криптостойкая генерация 6-значного числового кода.
        /// </summary>
        public string GenerateSecureCode()
        {
            byte[] randomNumber = new byte[4];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
            }

            int value = Math.Abs(BitConverter.ToInt32(randomNumber, 0));
            int code = 100000 + (value % 900000);
            return code.ToString("D6");
        }

        #endregion

        #region Pre-Validation Database Checks

        /// <summary>
        /// Проверяет доступность логина и email в базе данных SQLite через параметризированные запросы.
        /// </summary>
        public bool ValidateCredentialsAvailability(string login, string email, out string conflictError)
        {
            conflictError = string.Empty;

            if (DatabaseManager.Instance == null)
            {
                conflictError = "База данных недоступна!";
                return false;
            }

            try
            {
                // Проверка логина
                string checkLoginQuery = "SELECT COUNT(*) FROM users WHERE LOWER(login) = LOWER(@login);";
                object loginCount = DatabaseManager.Instance.ExecuteScalar(
                    checkLoginQuery,
                    new SqliteParameter("@login", login.Trim())
                );

                if (loginCount != null && Convert.ToInt32(loginCount) > 0)
                {
                    conflictError = "Герой с таким именем уже существует!";
                    return false;
                }

                // Проверка email
                string checkEmailQuery = "SELECT COUNT(*) FROM users WHERE LOWER(email) = LOWER(@email);";
                object emailCount = DatabaseManager.Instance.ExecuteScalar(
                    checkEmailQuery,
                    new SqliteParameter("@email", email.Trim().ToLowerInvariant())
                );

                if (emailCount != null && Convert.ToInt32(emailCount) > 0)
                {
                    conflictError = "Этот Email уже привязан к другому аккаунту!";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthController] Ошибка проверки уникальности в БД: {ex.Message}");
                conflictError = "Ошибка обращения к базе данных.";
                return false;
            }
        }

        #endregion

        #region Registration & Verification Flow

        /// <summary>
        /// Инициирует регистрацию: проверяет БД на уникальность, хэширует пароль по SHA-256
        /// и асинхронно отправляет код подтверждения на почту.
        /// </summary>
        public async Task<bool> StartRegistrationAsync(string login, string email, string rawPassword)
        {
            if (_isCurrentlySending)
            {
                OnCodeSendFailed?.Invoke("Отправка уже выполняется, подождите...");
                return false;
            }

            // 1. Проверка адреса по стандарту RFC 5322
            if (!EmailValidator.ValidateEmail(email, out string sanitizedEmail, out string validationError))
            {
                OnCodeSendFailed?.Invoke(validationError);
                return false;
            }

            // 2. Проверка уникальности логина и email в таблице users
            if (!ValidateCredentialsAvailability(login, sanitizedEmail, out string conflictError))
            {
                OnCodeSendFailed?.Invoke(conflictError);
                return false;
            }

            // 3. Криптографическое хэширование пароля (в памяти сохраняется только хэш SHA-256)
            _pendingLogin = login.Trim();
            _pendingEmail = sanitizedEmail;
            _pendingPasswordHash = PasswordSecurity.HashPassword(rawPassword);
            _isRegistrationPending = true;

            // 4. Запуск отправки письма
            return await SendVerificationCodeAsync(sanitizedEmail, isPasswordReset: false);
        }

        /// <summary>
        /// Потокобезопасная отправка письма с кодом подтверждения по протоколу SMTP.
        /// Сетевая часть выполняется в фоне, а все события и логи возвращаются в Главный Поток Unity.
        /// </summary>
        public async Task<bool> SendVerificationCodeAsync(string targetEmail, bool isPasswordReset = false)
        {
            if (_isCurrentlySending)
            {
                OnCodeSendFailed?.Invoke("Отправка уже выполняется, подождите...");
                return false;
            }

            if (!EmailValidator.ValidateEmail(targetEmail, out string sanitizedEmail, out string validationError))
            {
                Debug.LogWarning($"[AuthController] Ошибка валидации email: {validationError}");
                OnCodeSendFailed?.Invoke(validationError);
                return false;
            }

            _isCurrentlySending = true;
            _activeTargetEmail = sanitizedEmail;
            _activeGeneratedCode = GenerateSecureCode();
            _codeExpirationTimestamp = DateTime.UtcNow.AddSeconds(codeLifetimeSeconds);

            if (mockMode)
            {
                await Task.Delay(800);
                string modeLabel = isPasswordReset ? "СБРОС ПАРОЛЯ" : "РЕГИСТРАЦИЯ";
                Debug.Log($"<color=#00F0FF><b>[AuthController SMTP MOCK - {modeLabel}]</b></color> Письмо 'отправлено' на: <b>{_activeTargetEmail}</b>\n" +
                          $"<color=#00FF66><b>СЕКРЕТНЫЙ КОД:</b></color> <size=16><b>{_activeGeneratedCode}</b></size> (Действует 5 мин)");

                _isCurrentlySending = false;
                OnCodeSentSuccessfully?.Invoke(_activeTargetEmail);
                return true;
            }

            // Подготавливаем локальные строковые копии данных для безопасного пула потоков
            string cleanHost = smtpHost.Trim();
            int port = smtpPort;
            bool ssl = enableSsl;
            string cleanSender = senderEmail.Trim();
            string cleanPass = senderPassword.Replace(" ", "").Trim();
            string displayName = senderDisplayName;
            string recipient = _activeTargetEmail;
            string subject = isPasswordReset
                ? "SOLUTIO — Восстановление доступа к аккаунту"
                : "SOLUTIO — Код подтверждения профиля";
            string body = BuildHtmlEmailBody(_activeGeneratedCode, (int)(codeLifetimeSeconds / 60), isPasswordReset);

            bool sendSuccess = false;
            string failureReason = string.Empty;

            // ЧИСТАЯ СЕТЕВАЯ ПЕРЕДАЧА В ФОНЕ (без единого вызова API Unity)
            await Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

                    using (MailMessage mail = new MailMessage())
                    {
                        mail.From = new MailAddress(cleanSender, displayName, System.Text.Encoding.UTF8);
                        mail.To.Add(recipient);
                        mail.Subject = subject;
                        mail.SubjectEncoding = System.Text.Encoding.UTF8;
                        mail.Body = body;
                        mail.BodyEncoding = System.Text.Encoding.UTF8;
                        mail.IsBodyHtml = true;

                        using (SmtpClient smtp = new SmtpClient(cleanHost, port))
                        {
                            smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                            smtp.UseDefaultCredentials = false;
                            smtp.Credentials = new NetworkCredential(cleanSender, cleanPass);
                            smtp.EnableSsl = ssl;
                            smtp.Timeout = 15000;
                            smtp.Send(mail);
                        }
                    }

                    sendSuccess = true;
                }
                catch (Exception ex)
                {
                    failureReason = ex.Message;
                    sendSuccess = false;
                }
            });

            // МЫ СНОВА В ГЛАВНОМ ПОТОКЕ UNITY (Main Thread)
            _isCurrentlySending = false;

            if (sendSuccess)
            {
                Debug.Log($"<color=#00FF66>[AuthController] Письмо УСПЕШНО отправлено на {_activeTargetEmail} через SMTP!</color>");
                OnCodeSentSuccessfully?.Invoke(_activeTargetEmail);
                return true;
            }
            else
            {
                Debug.LogError($"<color=red>[AuthController SMTP Error]</color> Не удалось отправить письмо: {failureReason}");
                OnCodeSendFailed?.Invoke("Ошибка почтового сервера при отправке кода.");
                return false;
            }
        }

        /// <summary>
        /// Проверяет введенный пользователем код. Если код верный и активна регистрация —
        /// автоматически записывает пользователя в базу данных SQLite с хэшем SHA-256.
        /// </summary>
        public bool VerifyCode(string enteredCode, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(_activeGeneratedCode))
            {
                errorMessage = "Код не запрашивался или сессия сброшена!";
                return false;
            }

            if (DateTime.UtcNow > _codeExpirationTimestamp)
            {
                errorMessage = "Срок действия кода истек! Запросите новый код.";
                return false;
            }

            string cleanEnteredCode = enteredCode?.Trim() ?? string.Empty;

            if (!string.Equals(_activeGeneratedCode, cleanEnteredCode, StringComparison.Ordinal))
            {
                errorMessage = "Неверный код подтверждения!";
                return false;
            }

            Debug.Log($"<color=#00FF66><b>[AuthController]</b> Email {_activeTargetEmail} успешно верифицирован!</color>");
            OnEmailVerified?.Invoke(_activeTargetEmail);
            _activeGeneratedCode = string.Empty;

            // Если подтверждалась регистрация нового пользователя — вносим запись в БД
            if (_isRegistrationPending)
            {
                return CommitRegistrationToDatabase(out errorMessage);
            }

            return true;
        }

        /// <summary>
        /// Вносит верифицированного пользователя в таблицу users базы SQLite через параметризированную команду.
        /// </summary>
        private bool CommitRegistrationToDatabase(out string errorMessage)
        {
            errorMessage = string.Empty;

            if (DatabaseManager.Instance == null)
            {
                errorMessage = "Ошибка: Сервис базы данных недоступен!";
                OnRegistrationDatabaseError?.Invoke(errorMessage);
                return false;
            }

            const string insertQuery = @"
                INSERT INTO users (login, password_hash, email) 
                VALUES (@login, @password_hash, @email);";

            SqliteParameter[] parameters = new SqliteParameter[]
            {
                new SqliteParameter("@login", _pendingLogin),
                new SqliteParameter("@password_hash", _pendingPasswordHash),
                new SqliteParameter("@email", _pendingEmail)
            };

            int rowsAffected = DatabaseManager.Instance.ExecuteNonQuery(insertQuery, parameters);

            if (rowsAffected > 0)
            {
                // Считываем присвоенный автоинкрементный id_user
                object lastIdObj = DatabaseManager.Instance.ExecuteScalar("SELECT last_insert_rowid();");
                int newUserId = (lastIdObj != null) ? Convert.ToInt32(lastIdObj) : -1;

                Debug.Log($"<color=#00FF66><b>[DATABASE] ПОЛЬЗОВАТЕЛЬ УСПЕШНО ЗАРЕГИСТРИРОВАН!</b></color>\n" +
                          $"ID: <b>{newUserId}</b> | Логин: <b>{_pendingLogin}</b> | Email: <b>{_pendingEmail}</b>\n" +
                          $"SHA-256 Хэш: <color=#307082>{_pendingPasswordHash}</color>");

                string registeredLogin = _pendingLogin;

                // Очищаем буфер чувствительных данных
                _pendingLogin = string.Empty;
                _pendingEmail = string.Empty;
                _pendingPasswordHash = string.Empty;
                _isRegistrationPending = false;

                OnUserRegisteredSuccessfully?.Invoke(newUserId, registeredLogin);
                return true;
            }
            else
            {
                errorMessage = "Не удалось сохранить учетную запись в базе данных!";
                Debug.LogError($"[AuthController] {errorMessage}");
                OnRegistrationDatabaseError?.Invoke(errorMessage);
                return false;
            }
        }

        #endregion

        #region Dark Fantasy HTML Template

        private string BuildHtmlEmailBody(string code, int lifetimeMinutes, bool isPasswordReset)
        {
            string title = isPasswordReset ? "ВОССТАНОВЛЕНИЕ ДОСТУПА" : "ВЕРИФИКАЦИЯ ПРОФИЛЯ ГЕРОЯ";
            string desc = isPasswordReset
                ? "Древние печати зафиксировали запрос на сброс пароля от вашей учетной записи в <b>Solutio</b>. Введите этот тайный ключ для подтверждения вашей личности:"
                : "Приветствуем в мрачном мире <b>Solutio</b>, путник. Для привязки вашей души к защищенному реестру базы данных введите этот рунический код:";

            return $@"
            <div style='background-color: #05070B; padding: 40px 15px; font-family: Georgia, ""Times New Roman"", serif;'>
                <div style='background-color: #0A0D14; max-width: 520px; margin: 0 auto; border: 2px solid #162530; border-radius: 4px; padding: 35px 25px; box-shadow: 0 0 30px rgba(0, 0, 0, 0.9); text-align: center;'>
                    <div style='color: #307082; font-size: 18px; margin-bottom: 5px; letter-spacing: 6px;'>❖ ❖ ❖</div>
                    <h1 style='color: #E6F4F8; font-size: 32px; font-weight: bold; letter-spacing: 8px; margin: 5px 0 0 0; text-transform: uppercase;'>SOLUTIO</h1>
                    <p style='color: #4A6E7D; font-size: 11px; letter-spacing: 3px; margin: 5px 0 15px 0; text-transform: uppercase;'>ТАКТИЧЕСКИЙ СИМУЛЯТОР // ВРАТА ТЬМЫ</p>
                    <div style='color: #307082; font-size: 14px; margin: 15px 0 25px 0;'>─── ✦ {title} ✦ ───</div>
                    <p style='color: #A0B2C6; font-size: 14px; line-height: 1.6; text-align: left; margin: 0 10px 25px 10px;'>
                        {desc}
                    </p>
                    <div style='background-color: #06080E; border: 1px solid #307082; border-radius: 4px; padding: 20px 10px; margin: 0 10px 25px 10px; box-shadow: inset 0 0 15px rgba(0,0,0,0.8);'>
                        <span style='font-family: ""Courier New"", monospace; font-size: 36px; font-weight: bold; letter-spacing: 12px; color: #4DA3B8; text-shadow: 0 0 10px rgba(48, 112, 130, 0.8); display: inline-block; padding-left: 12px;'>
                            {code}
                        </span>
                    </div>
                    <p style='color: #556677; font-size: 12px; margin: 0 10px 20px 10px; line-height: 1.4;'>
                        Печать рассеется через <span style='color: #307082; font-weight: bold;'>{lifetimeMinutes} минут</span>. Если этот зов потревожил вас по ошибке — просто проигнорируйте его, ваши тайны останутся нетронутыми.
                    </p>
                    <div style='border-top: 1px solid #14202B; margin: 25px 20px 15px 20px;'></div>
                    <p style='color: #384552; font-size: 10px; letter-spacing: 2px; margin: 0;'>© 2026 SOLUTIO GAME ENGINE. ВСЕ ПРАВА ЗАЩИЩЕНЫ.</p>
                </div>
            </div>";
        }

        #endregion
    }
}
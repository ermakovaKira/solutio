using System;
using System.Security.Cryptography;
using System.Text;

namespace Solutio.Auth
{
    /// <summary>
    /// Криптографический модуль хэширования и верификации паролей пользователей.
    /// Использует стандарт SHA-256 без сторонних зависимостей.
    /// </summary>
    public static class PasswordSecurity
    {
        /// <summary>
        /// Вычисляет криптографический хэш SHA-256 от переданной строки пароля.
        /// Возвращает 64-символьную шестнадцатеричную строку в нижнем регистре.
        /// </summary>
        /// <param name="rawPassword">Открытый пароль пользователя.</param>
        /// <returns>Хэш SHA-256.</returns>
        public static string HashPassword(string rawPassword)
        {
            if (string.IsNullOrEmpty(rawPassword))
            {
                return string.Empty;
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(rawPassword);
                byte[] hashBytes = sha256.ComputeHash(inputBytes);

                // Оптимизированное формирование hex-строки
                StringBuilder stringBuilder = new StringBuilder(hashBytes.Length * 2);
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    stringBuilder.Append(hashBytes[i].ToString("x2"));
                }

                return stringBuilder.ToString();
            }
        }

        /// <summary>
        /// Безопасно сверяет сырой пароль с сохраненным в базе данных хэшем.
        /// </summary>
        /// <param name="rawPassword">Введенный пароль для проверки.</param>
        /// <param name="storedHash">Хэш из базы данных.</param>
        /// <returns>True, если хэши совпадают, иначе False.</returns>
        public static bool VerifyPassword(string rawPassword, string storedHash)
        {
            if (string.IsNullOrEmpty(rawPassword) || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }

            string computedHash = HashPassword(rawPassword);
            return string.Equals(computedHash, storedHash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
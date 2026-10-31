using System;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Solutio.Auth
{
    /// <summary>
    /// Статический модуль строгой валидации и санитарной обработки адресов электронной почты.
    /// Соответствует стандартам RFC 5322 и защищает систему от передачи некорректных адресов.
    /// </summary>
    public static class EmailValidator
    {
        // Регулярное выражение международного стандарта RFC 5322 для email-адресов
        private static readonly Regex RfcEmailRegex = new Regex(
            @"^(?!\.)(""([^""\r\\]|\\[""\r\\])*""|"
            + @"([-a-z0-9!#$%&'*+/=?^_`{|}~]|(?<!\.)\.)*)"
            + @"(?<!\.)@[a-z0-9][\w\.-]*[a-z0-9]\.[a-z][a-z\.]*[a-z]$",
            RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);

        /// <summary>
        /// Проверяет корректность формата email-адреса и возвращает очищенную нормализованную строку.
        /// </summary>
        /// <param name="rawEmail">Исходный введенный пользователем адрес.</param>
        /// <param name="sanitizedEmail">Очищенный адрес в нижнем регистре без пробелов.</param>
        /// <param name="errorMessage">Описание ошибки валидации в случае неудачи.</param>
        /// <returns>True, если адрес полностью валиден; иначе False.</returns>
        public static bool ValidateEmail(string rawEmail, out string sanitizedEmail, out string errorMessage)
        {
            sanitizedEmail = string.Empty;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(rawEmail))
            {
                errorMessage = "Поле Email не может быть пустым!";
                return false;
            }

            // Очищаем от случайных начальных и конечных пробелов
            string trimmed = rawEmail.Trim();

            // Проверка длины: стандарт не допускает адреса длиннее 254 символов
            if (trimmed.Length < 6 || trimmed.Length > 254)
            {
                errorMessage = "Длина Email должна быть от 6 до 254 символов!";
                return false;
            }

            // Проверка через парсер System.Net.Mail
            try
            {
                MailAddress mailAddress = new MailAddress(trimmed);
                if (mailAddress.Address != trimmed)
                {
                    errorMessage = "Некорректная структура почтового адреса!";
                    return false;
                }
            }
            catch (FormatException)
            {
                errorMessage = "Неверный формат адреса электронной почты!";
                return false;
            }

            // Проверка через скомпилированное регулярное выражение
            if (!RfcEmailRegex.IsMatch(trimmed))
            {
                errorMessage = "Адрес содержит недопустимые символы или неверный домен!";
                return false;
            }

            // Проверка доменной части (наличие точки и валидной зоны)
            string[] parts = trimmed.Split('@');
            if (parts.Length != 2 || !parts[1].Contains('.'))
            {
                errorMessage = "Отсутствует корректная доменная зона (например, .com, .ru, .by)!";
                return false;
            }

            // Нормализация в нижний регистр для безопасной записи в БД
            sanitizedEmail = trimmed.ToLowerInvariant();
            return true;
        }

        /// <summary>
        /// Быстрая булевая проверка валидности адреса.
        /// </summary>
        public static bool IsValid(string rawEmail)
        {
            return ValidateEmail(rawEmail, out _, out _);
        }
    }
}
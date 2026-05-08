using Microsoft.VisualBasic.FileIO;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.IO;
using System.Linq;

namespace ArchiveExtractor
{
    class Program
    {
        private static bool deleteAfterExtraction = false;
        private static string[] passwordList = Array.Empty<string>();
        private static int totalFound = 0;
        private static int extractedWithoutPassword = 0;
        private static int extractedWithPassword = 0;
        private static int passwordProtectedSkipped = 0;
        private static int errors = 0;

        static void Main(string[] args)
        {
            Console.WriteLine("=== Распаковка архивов с подбором пароля ===\n");

            // Запрос пути к папке
            Console.WriteLine("Введите путь к папке для поиска архивов:");
            string folderPath = Console.ReadLine();

            if (!Directory.Exists(folderPath))
            {
                Console.WriteLine("Указанная папка не существует!");
                return;
            }

            // Настройка удаления
            Console.WriteLine("Удалять архивы после успешной распаковки? (y/n):");
            deleteAfterExtraction = Console.ReadLine().ToLower() == "y";

            // Загрузка списка паролей
            LoadPasswordList();

            Console.WriteLine("\nНачинаю обработку...\n");

            // Поддерживаемые форматы архивов
            string[] archiveExtensions = { "*.zip", "*.rar", "*.7z", "*.tar", "*.gz", "*.001" };

            foreach (var extension in archiveExtensions)
            {
                string[] archiveFiles = Directory.GetFiles(folderPath, extension, System.IO.SearchOption.AllDirectories);

                foreach (var archivePath in archiveFiles)
                {
                    totalFound++;
                    Console.WriteLine($"[{totalFound}] Найден архив: {Path.GetFileName(archivePath)}");

                    // Пытаемся распаковать архив
                    bool extracted = TryExtractArchive(archivePath);

                    if (!extracted)
                    {
                        // Если архив не распакован, считаем его пропущенным (с паролем или ошибка)
                        try
                        {
                            using (var testArchive = ArchiveFactory.Open(archivePath))
                            {
                                // Если открывается без пароля, значит ошибка не в пароле
                                errors++;
                            }
                        }
                        catch (CryptographicException)
                        {
                            passwordProtectedSkipped++;
                        }
                        catch
                        {
                            errors++;
                        }
                    }
                }
            }

            PrintStatistics();
            Console.WriteLine("\nОбработка завершена!");
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }

        static void LoadPasswordList()
        {
            Console.WriteLine("Загрузить пароли из файла (укажите путь к файлу):");
            var passwordFile = Console.ReadLine();
            if (File.Exists(passwordFile))
            {
                passwordList = File.ReadAllLines(passwordFile)
                                    .Where(line => !string.IsNullOrWhiteSpace(line))
                                    .ToArray();
                Console.WriteLine($"Загружено паролей: {passwordList.Length}");
            }
            else
            {
                Console.WriteLine($"Файл {passwordFile} не найден. Будет использован пустой список паролей.");
            }
        }

        static bool TryExtractArchive(string archivePath)
        {
            // Сначала пробуем без пароля
            if (TryExtractWithPassword(archivePath, null))
            {
                extractedWithoutPassword++;
                return true;
            }

            // Если не получилось, пробуем каждый пароль из списка
            foreach (var password in passwordList)
            {
                Console.WriteLine($"  Подбор пароля: {password}");
                if (TryExtractWithPassword(archivePath, password))
                {
                    extractedWithPassword++;
                    return true;
                }
            }

            return false;
        }

        static bool TryExtractWithPassword(string archivePath, string password)
        {
            string extractPath = "";

            try
            {
                // Создаём путь для распаковки
                extractPath = CreateExtractPath(archivePath);
                Directory.CreateDirectory(extractPath);

                // Открываем архив с указанным паролем (если он есть)
                ReaderOptions readerOptions = new ReaderOptions { Password = password };
                using (var archive = ArchiveFactory.Open(archivePath, readerOptions))
                {
                    // Проверяем, есть ли в архиве файлы
                    if (!archive.Entries.Any(e => !e.IsDirectory))
                    {
                        Console.WriteLine("  Архив пуст.");
                        CleanupEmptyDirectory(extractPath);
                        return false;
                    }

                    // Распаковываем все файлы
                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
                            entry.WriteToDirectory(extractPath, new ExtractionOptions
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }
                }

                Console.WriteLine($"  Успешно распакован{(password != null ? " с паролем" : "")}.");
                Console.WriteLine($"  Расположение: {extractPath}");

                // Удаляем исходный архив, если разрешено
                if (deleteAfterExtraction)
                {
                    TryDeleteArchive(archivePath);
                }

                return true;
            }
            catch (CryptographicException)
            {
                // Неверный пароль
                CleanupEmptyDirectory(extractPath);
                return false;
            }
            catch (InvalidFormatException)
            {
                Console.WriteLine("  Ошибка: повреждённый или неподдерживаемый формат.");
                CleanupEmptyDirectory(extractPath);
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Ошибка: {ex.Message}");
                CleanupEmptyDirectory(extractPath);
                return false;
            }
        }

        static string CreateExtractPath(string archivePath)
        {
            string basePath = Path.GetDirectoryName(archivePath);
            string folderName = Path.GetFileNameWithoutExtension(archivePath) + "_extracted";

            string extractPath = Path.Combine(basePath, folderName);

            // Если папка уже существует, добавляем номер
            if (Directory.Exists(extractPath))
            {
                int counter = 1;
                string newPath;
                do
                {
                    newPath = extractPath + $"_{counter}";
                    counter++;
                } while (Directory.Exists(newPath));
                extractPath = newPath;
            }

            return extractPath;
        }

        static void CleanupEmptyDirectory(string directoryPath)
        {
            try
            {
                if (Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath);
                }
            }
            catch { }
        }

        static void TryDeleteArchive(string archivePath)
        {
            try
            {
                MoveToRecycleBin(archivePath);
                Console.WriteLine($"  Архив удалён: {Path.GetFileName(archivePath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Не удалось удалить архив: {ex.Message}");
            }
        }

        static void PrintStatistics()
        {
            Console.WriteLine("\n=== Результаты обработки ===");
            Console.WriteLine($"Найдено архивов: {totalFound}");
            Console.WriteLine($"Распаковано без пароля: {extractedWithoutPassword}");
            Console.WriteLine($"Распаковано с подбором пароля: {extractedWithPassword}");
            Console.WriteLine($"Защищённых паролем (пропущено): {passwordProtectedSkipped}");
            Console.WriteLine($"Ошибок обработки: {errors}");

            if (deleteAfterExtraction)
            {
                Console.WriteLine($"Удалено архивов: {extractedWithoutPassword + extractedWithPassword}");
            }
        }

        static void MoveToRecycleBin(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    FileSystem.DeleteFile(filePath,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin);
                    Console.WriteLine($"  Архив перемещен в корзину: {Path.GetFileName(filePath)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Не удалось переместить в корзину: {ex.Message}");
            }
        }
    }
}
using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.VisualBasic.FileIO;

namespace EuroMillionsStatisticsManager
{
    public partial class MainWindow : Window
    {
        private readonly string archivesDirectory = Path.Combine(AppContext.BaseDirectory, "Archives");

        public MainWindow()
        {
            InitializeComponent();
            LoadHistory();
        }

        private void LoadHistory()
        {
            try
            {
                Directory.CreateDirectory(archivesDirectory);
            }
            catch (IOException exception)
            {
                ShowArchiveError(exception.Message);
                return;
            }
            catch (UnauthorizedAccessException exception)
            {
                ShowArchiveError(exception.Message);
                return;
            }

            var importedFiles = new List<CsvFileData>();
            var errors = new List<string>();
            string[] files;
            try
            {
                files = Directory.GetFiles(archivesDirectory, "*.csv");
            }
            catch (IOException exception)
            {
                ShowArchiveError(exception.Message);
                return;
            }
            catch (UnauthorizedAccessException exception)
            {
                ShowArchiveError(exception.Message);
                return;
            }

            foreach (var filePath in files)
            {
                try
                {
                    importedFiles.Add(ReadCsvFile(filePath));
                }
                catch (IOException exception)
                {
                    errors.Add($"{Path.GetFileName(filePath)} : {exception.Message}");
                }
                catch (UnauthorizedAccessException exception)
                {
                    errors.Add($"{Path.GetFileName(filePath)} : {exception.Message}");
                }
                catch (MalformedLineException exception)
                {
                    errors.Add($"{Path.GetFileName(filePath)}, ligne {exception.LineNumber} : {exception.Message}");
                }
            }

            var history = BuildHistoryTable(importedFiles);
            Historique.ItemsSource = history.DefaultView;
            StatusText.Text = $"{history.Rows.Count} tirage(s) chargé(s) depuis {importedFiles.Count} fichier(s).";

            if (errors.Count > 0)
            {
                StatusText.Text += $" {errors.Count} fichier(s) n’ont pas pu être importés.";
                MessageBox.Show(
                    string.Join(Environment.NewLine, errors),
                    "Erreurs de chargement des archives",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ShowArchiveError(string message)
        {
            StatusText.Text = "Impossible de créer ou d’accéder au dossier Archives.";
            MessageBox.Show(
                $"Le dossier d’archives n’a pas pu être créé ou ouvert :{Environment.NewLine}{message}",
                "Erreur d’accès aux archives",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private static CsvFileData ReadCsvFile(string filePath)
        {
            var delimiter = DetectDelimiter(filePath);
            using var parser = new TextFieldParser(filePath, Encoding.UTF8)
            {
                HasFieldsEnclosedInQuotes = true,
                TrimWhiteSpace = false
            };
            parser.SetDelimiters(delimiter);

            var headers = parser.ReadFields();
            if (headers is null || headers.Length == 0)
            {
                return new CsvFileData(Path.GetFileName(filePath), [], []);
            }

            var normalizedHeaders = MakeHeadersUnique(headers);
            var rows = new List<string[]>();
            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields();
                if (fields is not null && fields.Any(field => !string.IsNullOrWhiteSpace(field)))
                {
                    rows.Add(fields);
                }
            }

            return new CsvFileData(Path.GetFileName(filePath), normalizedHeaders, rows);
        }

        private static string DetectDelimiter(string filePath)
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var firstLine = reader.ReadLine() ?? string.Empty;
            var commaCount = CountDelimitersOutsideQuotes(firstLine, ',');
            var semicolonCount = CountDelimitersOutsideQuotes(firstLine, ';');
            var tabCount = CountDelimitersOutsideQuotes(firstLine, '\t');

            if (tabCount > commaCount && tabCount > semicolonCount)
            {
                return "\t";
            }

            return semicolonCount > commaCount ? ";" : ",";
        }

        private static int CountDelimitersOutsideQuotes(string line, char delimiter)
        {
            var count = 0;
            var insideQuotes = false;

            for (var index = 0; index < line.Length; index++)
            {
                if (line[index] == '"')
                {
                    if (insideQuotes && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        index++;
                    }
                    else
                    {
                        insideQuotes = !insideQuotes;
                    }
                }
                else if (!insideQuotes && line[index] == delimiter)
                {
                    count++;
                }
            }

            return count;
        }

        private static string[] MakeHeadersUnique(string[] headers)
        {
            var usedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Fichier source" };
            var normalizedHeaders = new string[headers.Length];

            for (var index = 0; index < headers.Length; index++)
            {
                var baseHeader = string.IsNullOrWhiteSpace(headers[index])
                    ? $"Colonne {index + 1}"
                    : headers[index].Trim();
                var header = baseHeader;
                var occurrence = 2;
                while (!usedHeaders.Add(header))
                {
                    header = $"{baseHeader} ({occurrence})";
                    occurrence++;
                }

                normalizedHeaders[index] = header;
            }

            return normalizedHeaders;
        }

        private static DataTable BuildHistoryTable(IEnumerable<CsvFileData> files)
        {
            var history = new DataTable();
            var sourceColumnName = "Fichier source";
            history.Columns.Add(sourceColumnName, typeof(string));

            foreach (var file in files)
            {
                foreach (var header in file.Headers)
                {
                    if (!history.Columns.Contains(header))
                    {
                        history.Columns.Add(header, typeof(string));
                    }
                }
            }

            foreach (var file in files)
            {
                foreach (var fields in file.Rows)
                {
                    var row = history.NewRow();
                    row[sourceColumnName] = file.FileName;
                    for (var index = 0; index < file.Headers.Length && index < fields.Length; index++)
                    {
                        row[file.Headers[index]] = fields[index];
                    }

                    history.Rows.Add(row);
                }
            }

            return history;
        }

        private sealed record CsvFileData(string FileName, string[] Headers, List<string[]> Rows);
    }
}
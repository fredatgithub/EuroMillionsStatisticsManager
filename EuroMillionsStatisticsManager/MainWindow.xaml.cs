using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.VisualBasic.FileIO;

namespace EuroMillionsStatisticsManager
{
  public partial class MainWindow: Window
  {
    private readonly string archivesDirectory = Path.Combine(AppContext.BaseDirectory, "Archives");
    private readonly string windowSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EuroMillionsStatisticsManager",
        "window-settings.json");

    public MainWindow()
    {
      InitializeComponent();
      var version = typeof(MainWindow).Assembly.GetName().Version;
      Title = $"{Title} - V{version?.ToString(3) ?? "1.0.0"}";
      RestoreWindowSettings();
      Closing += (_, _) => SaveWindowSettings();
      LoadHistory();
    }

    private void RestoreWindowSettings()
    {
      try
      {
        if (!File.Exists(windowSettingsPath))
        {
          return;
        }

        var settings = JsonSerializer.Deserialize<WindowSettings>(File.ReadAllText(windowSettingsPath));
        if (settings is null
            || !double.IsFinite(settings.Left)
            || !double.IsFinite(settings.Top)
            || !double.IsFinite(settings.Width)
            || !double.IsFinite(settings.Height)
            || settings.Width < MinWidth
            || settings.Height < MinHeight)
        {
          return;
        }

        var bounds = new Rect(settings.Left, settings.Top, settings.Width, settings.Height);
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var visibleWidth = Math.Min(bounds.Right, virtualScreen.Right) - Math.Max(bounds.Left, virtualScreen.Left);
        var visibleHeight = Math.Min(bounds.Bottom, virtualScreen.Bottom) - Math.Max(bounds.Top, virtualScreen.Top);
        if (visibleWidth < 100 || visibleHeight < 100)
        {
          return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = settings.Left;
        Top = settings.Top;
        Width = settings.Width;
        Height = settings.Height;
        WindowState = settings.IsMaximized ? WindowState.Maximized : WindowState.Normal;
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
      catch (JsonException)
      {
      }
    }

    private void SaveWindowSettings()
    {
      var bounds = WindowState == WindowState.Normal
          ? new Rect(Left, Top, Width, Height)
          : RestoreBounds;
      var settings = new WindowSettings(
          bounds.Left,
          bounds.Top,
          bounds.Width,
          bounds.Height,
          WindowState == WindowState.Maximized);

      try
      {
        Directory.CreateDirectory(Path.GetDirectoryName(windowSettingsPath)!);
        File.WriteAllText(windowSettingsPath, JsonSerializer.Serialize(settings));
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
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
      Statistiques.ItemsSource = BuildBallStatistics(importedFiles);
      StatistiquesEtoiles.ItemsSource = BuildStarStatistics(importedFiles);
      StatistiquesPairesEtoiles.ItemsSource = BuildStarPairStatistics(importedFiles);
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

    private static List<BallStatistic> BuildBallStatistics(IEnumerable<CsvFileData> files)
    {
      var counts = new int[51];
      var latestDraws = new DateTime?[51];
      var drawCount = 0;

      foreach (var file in files)
      {
        var ballColumns = file.Headers
            .Select((header, index) => (header, index))
            .Where(column => IsBallHeader(column.header))
            .Select(column => column.index)
            .ToArray();
        if (ballColumns.Length != 5)
        {
          continue;
        }

        var dateColumn = Array.FindIndex(file.Headers, IsDateHeader);
        foreach (var fields in file.Rows)
        {
          var balls = new HashSet<int>();
          foreach (var column in ballColumns)
          {
            if (column < fields.Length && int.TryParse(fields[column], out var number) && number is >= 1 and <= 50)
            {
              balls.Add(number);
            }
          }

          if (balls.Count != 5)
          {
            continue;
          }

          drawCount++;
          DateTime? drawDate = dateColumn >= 0 && dateColumn < fields.Length
              ? ParseDrawDate(fields[dateColumn])
              : null;

          foreach (var ball in balls)
          {
            counts[ball]++;
            if (drawDate.HasValue && (!latestDraws[ball].HasValue || drawDate > latestDraws[ball]))
            {
              latestDraws[ball] = drawDate;
            }
          }
        }
      }

      return Enumerable.Range(1, 50)
          .Select(number => new BallStatistic(
              number,
              counts[number],
              drawCount == 0 ? 0 : counts[number] * 100d / drawCount,
              latestDraws[number]))
          .ToList();
    }

    private static bool IsBallHeader(string header)
    {
      return header.StartsWith("boule_", StringComparison.OrdinalIgnoreCase)
          ? int.TryParse(header.AsSpan("boule_".Length), out var position) && position is >= 1 and <= 5
          : header.Length == 2 && (header[0] == 'N' || header[0] == 'n') && header[1] is >= '1' and <= '5';
    }

    private static List<StarStatistic> BuildStarStatistics(IEnumerable<CsvFileData> files)
    {
      var counts = new int[13];
      var latestDraws = new DateTime?[13];
      var drawCount = 0;

      foreach (var file in files)
      {
        var starColumns = file.Headers
            .Select((header, index) => (header, index))
            .Where(column => IsStarHeader(column.header))
            .Select(column => column.index)
            .ToArray();
        if (starColumns.Length != 2)
        {
          continue;
        }

        var dateColumn = Array.FindIndex(file.Headers, IsDateHeader);
        foreach (var fields in file.Rows)
        {
          var stars = new HashSet<int>();
          foreach (var column in starColumns)
          {
            if (column < fields.Length && int.TryParse(fields[column], out var number) && number is >= 1 and <= 12)
            {
              stars.Add(number);
            }
          }

          if (stars.Count != 2)
          {
            continue;
          }

          drawCount++;
          DateTime? drawDate = dateColumn >= 0 && dateColumn < fields.Length
              ? ParseDrawDate(fields[dateColumn])
              : null;

          foreach (var star in stars)
          {
            counts[star]++;
            if (drawDate.HasValue && (!latestDraws[star].HasValue || drawDate > latestDraws[star]))
            {
              latestDraws[star] = drawDate;
            }
          }
        }
      }

      return Enumerable.Range(1, 12)
          .Select(number => new StarStatistic(
              number,
              counts[number],
              drawCount == 0 ? 0 : counts[number] * 100d / drawCount,
              latestDraws[number]))
          .ToList();
    }

    private static bool IsStarHeader(string header)
    {
      return header.StartsWith("etoile_", StringComparison.OrdinalIgnoreCase)
          ? int.TryParse(header.AsSpan("etoile_".Length), out var position) && position is >= 1 and <= 2
          : header.Length == 2 && (header[0] == 'E' || header[0] == 'e') && header[1] is '1' or '2';
    }

    private static List<StarPairStatistic> BuildStarPairStatistics(IEnumerable<CsvFileData> files)
    {
      var counts = new int[13, 13];
      var latestDraws = new DateTime?[13, 13];
      var drawCount = 0;

      foreach (var file in files)
      {
        var starColumns = file.Headers
            .Select((header, index) => (header, index))
            .Where(column => IsStarHeader(column.header))
            .Select(column => column.index)
            .ToArray();
        if (starColumns.Length != 2)
        {
          continue;
        }

        var dateColumn = Array.FindIndex(file.Headers, IsDateHeader);
        foreach (var fields in file.Rows)
        {
          var stars = new HashSet<int>();
          foreach (var column in starColumns)
          {
            if (column < fields.Length && int.TryParse(fields[column], out var number) && number is >= 1 and <= 12)
            {
              stars.Add(number);
            }
          }

          if (stars.Count != 2)
          {
            continue;
          }

          drawCount++;
          var firstStar = stars.Min();
          var secondStar = stars.Max();
          DateTime? drawDate = dateColumn >= 0 && dateColumn < fields.Length
              ? ParseDrawDate(fields[dateColumn])
              : null;
          counts[firstStar, secondStar]++;
          if (drawDate.HasValue && (!latestDraws[firstStar, secondStar].HasValue || drawDate > latestDraws[firstStar, secondStar]))
          {
            latestDraws[firstStar, secondStar] = drawDate;
          }
        }
      }

      return Enumerable.Range(1, 11)
          .SelectMany(firstStar => Enumerable.Range(firstStar + 1, 12 - firstStar)
              .Select(secondStar => new StarPairStatistic(
                  $"{firstStar}-{secondStar}",
                  counts[firstStar, secondStar],
                  drawCount == 0 ? 0 : counts[firstStar, secondStar] * 100d / drawCount,
                  latestDraws[firstStar, secondStar])))
          .ToList();
    }

    private static bool IsDateHeader(string header)
    {
      return string.Equals(header, "date_de_tirage", StringComparison.OrdinalIgnoreCase)
          || string.Equals(header, "DATE", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? ParseDrawDate(string value)
    {
      string[] formats = ["dd/MM/yyyy", "yyyyMMdd", "yyyy-MM-dd", "dd-MM-yyyy", "MM/dd/yyyy"];
      return DateTime.TryParseExact(
          value.Trim(),
          formats,
          CultureInfo.InvariantCulture,
          DateTimeStyles.None,
          out var date)
          ? date
          : null;
    }

    private sealed record CsvFileData(string FileName, string[] Headers, List<string[]> Rows);

    private sealed record WindowSettings(double Left, double Top, double Width, double Height, bool IsMaximized);

    private sealed record BallStatistic(int Numero, int NombreDeSorties, double PourcentageDeSortie, DateTime? DerniereSortie);

    private sealed record StarStatistic(int Numero, int NombreDeSorties, double PourcentageDeSortie, DateTime? DerniereSortie);

    private sealed record StarPairStatistic(string Couple, int NombreDeSorties, double PourcentageDeSortie, DateTime? DerniereSortie);
  }
}
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

    private readonly string logFilePath = Path.Combine(AppContext.BaseDirectory, $"EuroMillionsStatisticsManager-{DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}.log");

    private readonly string windowSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EuroMillionsStatisticsManager", "window-settings.json");

    private bool logFileAvailable;
    private bool logWriteErrorReported;

    public MainWindow()
    {
      InitializeComponent();
      InitializeLog();
      LogAction("Démarrage de l’application.");
      CreateArchivesDirectory();
      SetTitleVersion();
      RestoreWindowSettings();
      Closing += (_, _) =>
      {
        LogAction("Fermeture de l’application.");
        SaveWindowSettings();
      };

      LoadHistory();
    }

    private void InitializeLog()
    {
      try
      {
        File.WriteAllText(logFilePath, string.Empty, new UTF8Encoding(false));
        logFileAvailable = true;
      }
      catch (IOException exception)
      {
        ReportLogWriteError(exception.Message);
      }
      catch (UnauthorizedAccessException exception)
      {
        ReportLogWriteError(exception.Message);
      }
    }

    private void LogAction(string action)
    {
      var entry = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} - {action}";
      LogText.AppendText(entry + Environment.NewLine);
      LogText.ScrollToEnd();

      if (!logFileAvailable)
      {
        return;
      }

      try
      {
        File.AppendAllText(logFilePath, entry + Environment.NewLine, new UTF8Encoding(false));
      }
      catch (IOException exception)
      {
        logFileAvailable = false;
        ReportLogWriteError(exception.Message);
      }
      catch (UnauthorizedAccessException exception)
      {
        logFileAvailable = false;
        ReportLogWriteError(exception.Message);
      }
    }

    private void ReportLogWriteError(string message)
    {
      if (logWriteErrorReported)
      {
        return;
      }

      logWriteErrorReported = true;
      MessageBox.Show($"Le fichier journal n’a pas pu être créé ou mis à jour :{Environment.NewLine}{message}",
          "Erreur d’écriture du journal",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
    }

    private void CreateArchivesDirectory()
    {
      try
      {
        if (!Directory.Exists(archivesDirectory))
        {
          Directory.CreateDirectory(archivesDirectory);
          LogAction("Création du dossier Archives.");
        }
        else
        {
          LogAction("Dossier Archives déjà existant.");
        }
      }
      catch (IOException exception)
      {
        LogAction($"Erreur d’accès au dossier Archives : {exception.Message}");
        ShowArchiveError(exception.Message);
      }
      catch (UnauthorizedAccessException exception)
      {
        LogAction($"Erreur d’accès au dossier Archives : {exception.Message}");
        ShowArchiveError(exception.Message);
      }
    }

    private void SetTitleVersion()
    {
      var version = typeof(MainWindow).Assembly.GetName().Version;
      Title = $"{Title} - V{version?.ToString(3) ?? "1.0.0"}";
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
      catch (IOException ioException)
      {
        LogAction($"Erreur de lecture des paramètres de la fenêtre : {ioException.Message}");
      }
      catch (UnauthorizedAccessException unauthorizedAccessException)
      {
        LogAction($"Erreur de lecture des paramètres de la fenêtre : {unauthorizedAccessException.Message}");
      }
      catch (JsonException jsonException)
      {
        LogAction($"Erreur de lecture des paramètres de la fenêtre : {jsonException.Message}");
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
      catch (IOException ioException)
      {
        LogAction($"Erreur de sauvegarde des paramètres de la fenêtre : {ioException.Message}");
      }
      catch (UnauthorizedAccessException unauthorizedAccessException)
      {
        LogAction($"Erreur de sauvegarde des paramètres de la fenêtre : {unauthorizedAccessException.Message}");
      }
    }

    private void LoadHistory()
    {
      LogAction("Chargement des fichiers CSV du dossier Archives.");
      try
      {
        Directory.CreateDirectory(archivesDirectory);
      }
      catch (IOException exception)
      {
        LogAction($"Échec du chargement des archives : {exception.Message}");
        ShowArchiveError(exception.Message);
        return;
      }
      catch (UnauthorizedAccessException exception)
      {
        LogAction($"Échec du chargement des archives : {exception.Message}");
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
        LogAction($"Impossible de lister les fichiers CSV : {exception.Message}");
        ShowArchiveError(exception.Message);
        return;
      }
      catch (UnauthorizedAccessException exception)
      {
        LogAction($"Impossible de lister les fichiers CSV : {exception.Message}");
        ShowArchiveError(exception.Message);
        return;
      }

      foreach (var filePath in files)
      {
        try
        {
          importedFiles.Add(ReadCsvFile(filePath));
          LogAction($"Fichier chargé : {Path.GetFileName(filePath)}.");
        }
        catch (IOException exception)
        {
          errors.Add($"{Path.GetFileName(filePath)} : {exception.Message}");
          LogAction($"Erreur de lecture du fichier {Path.GetFileName(filePath)} : {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
          errors.Add($"{Path.GetFileName(filePath)} : {exception.Message}");
          LogAction($"Accès refusé au fichier {Path.GetFileName(filePath)} : {exception.Message}");
        }
        catch (MalformedLineException exception)
        {
          errors.Add($"{Path.GetFileName(filePath)}, ligne {exception.LineNumber} : {exception.Message}");
          LogAction($"Ligne mal formée dans {Path.GetFileName(filePath)}, ligne {exception.LineNumber} : {exception.Message}");
        }
      }

      var (distinctFiles, duplicateCount) = RemoveDuplicateDraws(importedFiles);
      var history = BuildHistoryTable(distinctFiles);
      Historique.ItemsSource = history.DefaultView;
      Statistiques.ItemsSource = BuildBallStatistics(distinctFiles);
      StatistiquesPairesBoules.ItemsSource = BuildBallPairStatistics(distinctFiles);
      StatistiquesTripletsBoules.ItemsSource = BuildBallTripletStatistics(distinctFiles);
      StatistiquesQuadrupletsBoules.ItemsSource = BuildBallQuadrupletStatistics(distinctFiles);
      StatistiquesQuintupletsBoules.ItemsSource = BuildBallQuintupletStatistics(distinctFiles);
      StatistiquesEtoiles.ItemsSource = BuildStarStatistics(distinctFiles);
      StatistiquesPairesEtoiles.ItemsSource = BuildStarPairStatistics(distinctFiles);
      StatusText.Text = $"{history.Rows.Count} tirage(s) chargé(s) depuis {importedFiles.Count} fichier(s).";
      if (duplicateCount > 0)
      {
        StatusText.Text += $" {duplicateCount} doublon(s) ignoré(s).";
        LogAction($"{duplicateCount} doublon(s) ignoré(s).");
      }

      LogAction($"{history.Rows.Count} tirage(s) chargé(s) depuis {importedFiles.Count} fichier(s) CSV.");
      if (errors.Count > 0)
      {
        StatusText.Text += $" {errors.Count} fichier(s) n’ont pas pu être importés.";
        LogAction($"Chargement terminé avec {errors.Count} erreur(s).");
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
      LogAction($"Erreur d’accès aux archives : {message}");
      MessageBox.Show($"Le dossier d’archives n’a pas pu être créé ou ouvert :{Environment.NewLine}{message}", "Erreur d’accès aux archives", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private static (List<CsvFileData> Files, int DuplicateCount) RemoveDuplicateDraws(IEnumerable<CsvFileData> files)
    {
      var seenDraws = new HashSet<string>(StringComparer.Ordinal);
      var distinctFiles = new List<CsvFileData>();
      var duplicateCount = 0;

      foreach (var file in files)
      {
        var ballColumns = file.Headers
            .Select((header, index) => (header, index))
            .Where(column => IsBallHeader(column.header))
            .Select(column => column.index)
            .ToArray();
        var starColumns = file.Headers
            .Select((header, index) => (header, index))
            .Where(column => IsStarHeader(column.header))
            .Select(column => column.index)
            .ToArray();
        var dateColumn = Array.FindIndex(file.Headers, IsDateHeader);

        if (ballColumns.Length != 5 || starColumns.Length != 2 || dateColumn < 0)
        {
          distinctFiles.Add(file);
          continue;
        }

        var distinctRows = new List<string[]>();
        foreach (var fields in file.Rows)
        {
          if (dateColumn >= fields.Length || ParseDrawDate(fields[dateColumn]) is not DateTime drawDate)
          {
            distinctRows.Add(fields);
            continue;
          }

          var balls = new HashSet<int>();
          foreach (var column in ballColumns)
          {
            if (column < fields.Length && int.TryParse(fields[column], out var number) && number is >= 1 and <= 50)
            {
              balls.Add(number);
            }
          }

          var stars = new HashSet<int>();
          foreach (var column in starColumns)
          {
            if (column < fields.Length && int.TryParse(fields[column], out var number) && number is >= 1 and <= 12)
            {
              stars.Add(number);
            }
          }

          if (balls.Count != 5 || stars.Count != 2)
          {
            distinctRows.Add(fields);
            continue;
          }

          var drawKey = $"{drawDate:yyyyMMdd}|{string.Join('-', balls.Order())}|{string.Join('-', stars.Order())}";
          if (seenDraws.Add(drawKey))
          {
            distinctRows.Add(fields);
          }
          else
          {
            duplicateCount++;
          }
        }

        distinctFiles.Add(file with { Rows = distinctRows });
      }

      return (distinctFiles, duplicateCount);
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
      var drawDates = new SortedSet<DateTime>?[51];
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
            drawDates[ball] = AddDrawDate(drawDates[ball], drawDate);
          }
        }
      }

      return Enumerable.Range(1, 50)
          .Select(number =>
          {
            var dates = GetRecentDrawDates(drawDates[number]);
            return new BallStatistic(
                number,
                counts[number],
                drawCount == 0 ? 0 : counts[number] * 100d / drawCount,
                dates.Latest,
                dates.Previous,
                dates.Third,
                dates.Fourth);
          })
          .ToList();
    }

    private static bool IsBallHeader(string header)
    {
      return header.StartsWith("boule_", StringComparison.OrdinalIgnoreCase)
          ? int.TryParse(header.AsSpan("boule_".Length), out var position) && position is >= 1 and <= 5
          : header.Length == 2 && (header[0] == 'N' || header[0] == 'n') && header[1] is >= '1' and <= '5';
    }

    private static List<BallPairStatistic> BuildBallPairStatistics(IEnumerable<CsvFileData> files)
    {
      var counts = new int[51, 51];
      var drawDates = new SortedSet<DateTime>?[51, 51];
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
          var orderedBalls = balls.Order().ToArray();
          DateTime? drawDate = dateColumn >= 0 && dateColumn < fields.Length
              ? ParseDrawDate(fields[dateColumn])
              : null;

          for (var firstIndex = 0; firstIndex < orderedBalls.Length - 1; firstIndex++)
          {
            var firstBall = orderedBalls[firstIndex];
            for (var secondIndex = firstIndex + 1; secondIndex < orderedBalls.Length; secondIndex++)
            {
              var secondBall = orderedBalls[secondIndex];
              counts[firstBall, secondBall]++;
              drawDates[firstBall, secondBall] = AddDrawDate(drawDates[firstBall, secondBall], drawDate);
            }
          }
        }
      }

      return Enumerable.Range(1, 49)
          .SelectMany(firstBall => Enumerable.Range(firstBall + 1, 50 - firstBall)
              .Select(secondBall =>
              {
                var dates = GetRecentDrawDates(drawDates[firstBall, secondBall]);
                return new BallPairStatistic(
                    $"{firstBall}-{secondBall}",
                    counts[firstBall, secondBall],
                    drawCount == 0 ? 0 : counts[firstBall, secondBall] * 100d / drawCount,
                    dates.Latest,
                    dates.Previous,
                    dates.Third,
                    dates.Fourth);
              }))
          .ToList();
    }

    private static List<BallTripletStatistic> BuildBallTripletStatistics(IEnumerable<CsvFileData> files)
    {
      var counts = new int[51, 51, 51];
      var drawDates = new SortedSet<DateTime>?[51, 51, 51];
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
          var orderedBalls = balls.Order().ToArray();
          DateTime? drawDate = dateColumn >= 0 && dateColumn < fields.Length
              ? ParseDrawDate(fields[dateColumn])
              : null;

          for (var firstIndex = 0; firstIndex < orderedBalls.Length - 2; firstIndex++)
          {
            var firstBall = orderedBalls[firstIndex];
            for (var secondIndex = firstIndex + 1; secondIndex < orderedBalls.Length - 1; secondIndex++)
            {
              var secondBall = orderedBalls[secondIndex];
              for (var thirdIndex = secondIndex + 1; thirdIndex < orderedBalls.Length; thirdIndex++)
              {
                var thirdBall = orderedBalls[thirdIndex];
                counts[firstBall, secondBall, thirdBall]++;
                drawDates[firstBall, secondBall, thirdBall] = AddDrawDate(
                    drawDates[firstBall, secondBall, thirdBall],
                    drawDate);
              }
            }
          }
        }
      }

      return Enumerable.Range(1, 48)
          .SelectMany(firstBall => Enumerable.Range(firstBall + 1, 49 - firstBall)
              .SelectMany(secondBall => Enumerable.Range(secondBall + 1, 50 - secondBall)
              .Select(thirdBall =>
              {
                var dates = GetRecentDrawDates(drawDates[firstBall, secondBall, thirdBall]);
                return new BallTripletStatistic(
                  $"{firstBall}-{secondBall}-{thirdBall}",
                  counts[firstBall, secondBall, thirdBall],
                  drawCount == 0 ? 0 : counts[firstBall, secondBall, thirdBall] * 100d / drawCount,
                  dates.Latest,
                  dates.Previous,
                  dates.Third,
                  dates.Fourth);
              })))
          .ToList();
    }

    private static List<BallQuadrupletStatistic> BuildBallQuadrupletStatistics(IEnumerable<CsvFileData> files)
    {
      var statistics = new Dictionary<
          (int First, int Second, int Third, int Fourth),
          BallQuadrupletAccumulator>();
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
          var orderedBalls = balls.Order().ToArray();
          DateTime? drawDate = dateColumn >= 0 && dateColumn < fields.Length
              ? ParseDrawDate(fields[dateColumn])
              : null;

          for (var firstIndex = 0; firstIndex < orderedBalls.Length - 3; firstIndex++)
          {
            for (var secondIndex = firstIndex + 1; secondIndex < orderedBalls.Length - 2; secondIndex++)
            {
              for (var thirdIndex = secondIndex + 1; thirdIndex < orderedBalls.Length - 1; thirdIndex++)
              {
                for (var fourthIndex = thirdIndex + 1; fourthIndex < orderedBalls.Length; fourthIndex++)
                {
                  var key = (
                      orderedBalls[firstIndex],
                      orderedBalls[secondIndex],
                      orderedBalls[thirdIndex],
                      orderedBalls[fourthIndex]);
                  if (!statistics.TryGetValue(key, out var current))
                  {
                    current = new BallQuadrupletAccumulator();
                    statistics.Add(key, current);
                  }

                  current.Count++;
                  current.DrawDates = AddDrawDate(current.DrawDates, drawDate);
                }
              }
            }
          }
        }
      }

      return statistics
          .OrderBy(entry => entry.Key.First)
          .ThenBy(entry => entry.Key.Second)
          .ThenBy(entry => entry.Key.Third)
          .ThenBy(entry => entry.Key.Fourth)
            .Select(entry =>
            {
              var dates = GetRecentDrawDates(entry.Value.DrawDates);
              return new BallQuadrupletStatistic(
                $"{entry.Key.First}-{entry.Key.Second}-{entry.Key.Third}-{entry.Key.Fourth}",
                entry.Value.Count,
                drawCount == 0 ? 0 : entry.Value.Count * 100d / drawCount,
                dates.Latest,
                dates.Previous,
                dates.Third,
                dates.Fourth);
            })
          .ToList();
    }

    private static List<BallQuintupletStatistic> BuildBallQuintupletStatistics(IEnumerable<CsvFileData> files)
    {
      var statistics = new Dictionary<
          (int First, int Second, int Third, int Fourth, int Fifth),
          BallQuintupletAccumulator>();
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
          var orderedBalls = balls.Order().ToArray();
          var key = (orderedBalls[0], orderedBalls[1], orderedBalls[2], orderedBalls[3], orderedBalls[4]);
          if (!statistics.TryGetValue(key, out var current))
          {
            current = new BallQuintupletAccumulator();
            statistics.Add(key, current);
          }

          current.Count++;
          DateTime? drawDate = dateColumn >= 0 && dateColumn < fields.Length
              ? ParseDrawDate(fields[dateColumn])
              : null;
          if (drawDate.HasValue)
          {
            current.DrawDates.Add(drawDate.Value);
          }
        }
      }

      return statistics
          .OrderBy(entry => entry.Key.First)
          .ThenBy(entry => entry.Key.Second)
          .ThenBy(entry => entry.Key.Third)
          .ThenBy(entry => entry.Key.Fourth)
          .ThenBy(entry => entry.Key.Fifth)
            .Select(entry =>
            {
              var dates = GetRecentDrawDates(entry.Value.DrawDates);
              return new BallQuintupletStatistic(
                $"{entry.Key.First}-{entry.Key.Second}-{entry.Key.Third}-{entry.Key.Fourth}-{entry.Key.Fifth}",
                entry.Value.Count,
                drawCount == 0 ? 0 : entry.Value.Count * 100d / drawCount,
                dates.Latest,
                dates.Previous,
                dates.Third,
                dates.Fourth);
            })
          .ToList();
    }

    private static List<StarStatistic> BuildStarStatistics(IEnumerable<CsvFileData> files)
    {
      var counts = new int[13];
      var drawDates = new SortedSet<DateTime>?[13];
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
            drawDates[star] = AddDrawDate(drawDates[star], drawDate);
          }
        }
      }

      return Enumerable.Range(1, 12)
          .Select(number =>
          {
            var dates = GetRecentDrawDates(drawDates[number]);
            return new StarStatistic(
              number,
              counts[number],
              drawCount == 0 ? 0 : counts[number] * 100d / drawCount,
              dates.Latest,
              dates.Previous,
              dates.Third,
              dates.Fourth);
          })
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
      var drawDates = new SortedSet<DateTime>?[13, 13];
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
          drawDates[firstStar, secondStar] = AddDrawDate(drawDates[firstStar, secondStar], drawDate);
        }
      }

      return Enumerable.Range(1, 11)
          .SelectMany(firstStar => Enumerable.Range(firstStar + 1, 12 - firstStar)
            .Select(secondStar =>
            {
              var dates = GetRecentDrawDates(drawDates[firstStar, secondStar]);
              return new StarPairStatistic(
                $"{firstStar}-{secondStar}",
                counts[firstStar, secondStar],
                drawCount == 0 ? 0 : counts[firstStar, secondStar] * 100d / drawCount,
                dates.Latest,
                dates.Previous,
                dates.Third,
                dates.Fourth);
            }))
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

    private static SortedSet<DateTime>? AddDrawDate(SortedSet<DateTime>? dates, DateTime? drawDate)
    {
      if (!drawDate.HasValue)
      {
        return dates;
      }

      dates ??= new SortedSet<DateTime>();
      dates.Add(drawDate.Value);
      return dates;
    }

    private static RecentDrawDates GetRecentDrawDates(SortedSet<DateTime>? dates)
    {
      var recentDates = dates?.Reverse().Take(4).ToArray() ?? [];
      return new RecentDrawDates(
          recentDates.Length > 0 ? recentDates[0] : null,
          recentDates.Length > 1 ? recentDates[1] : null,
          recentDates.Length > 2 ? recentDates[2] : null,
          recentDates.Length > 3 ? recentDates[3] : null);
    }

    private sealed record CsvFileData(string FileName, string[] Headers, List<string[]> Rows);

    private sealed record WindowSettings(double Left, double Top, double Width, double Height, bool IsMaximized);

    private sealed record BallStatistic(
      int Numero,
      int NombreDeSorties,
      double PourcentageDeSortie,
      DateTime? DerniereSortie,
      DateTime? AvantDerniereSortie,
      DateTime? AvantAvantDerniereSortie,
      DateTime? QuatriemeDerniereSortie);

    private sealed record BallPairStatistic(
      string Paire,
      int NombreDeSorties,
      double PourcentageDeSortie,
      DateTime? DerniereSortie,
      DateTime? AvantDerniereSortie,
      DateTime? AvantAvantDerniereSortie,
      DateTime? QuatriemeDerniereSortie);

    private sealed record BallTripletStatistic(
      string Triplet,
      int NombreDeSorties,
      double PourcentageDeSortie,
      DateTime? DerniereSortie,
      DateTime? AvantDerniereSortie,
      DateTime? AvantAvantDerniereSortie,
      DateTime? QuatriemeDerniereSortie);

    private sealed record BallQuadrupletStatistic(
      string Quadruplet,
      int NombreDeSorties,
      double PourcentageDeSortie,
      DateTime? DerniereSortie,
      DateTime? AvantDerniereSortie,
      DateTime? AvantAvantDerniereSortie,
      DateTime? QuatriemeDerniereSortie);

    private sealed record RecentDrawDates(
      DateTime? Latest,
      DateTime? Previous,
      DateTime? Third,
      DateTime? Fourth);

    private sealed class BallQuadrupletAccumulator
    {
      public int Count { get; set; }

      public SortedSet<DateTime>? DrawDates { get; set; }
    }

    private sealed class BallQuintupletAccumulator
    {
      public int Count { get; set; }

      public SortedSet<DateTime> DrawDates { get; } = new();
    }

    private sealed record BallQuintupletStatistic(
        string Quintuplet,
        int NombreDeSorties,
        double PourcentageDeSortie,
        DateTime? DerniereSortie,
        DateTime? AvantDerniereSortie,
        DateTime? AvantAvantDerniereSortie,
        DateTime? QuatriemeDerniereSortie);

    private sealed record StarStatistic(
      int Numero,
      int NombreDeSorties,
      double PourcentageDeSortie,
      DateTime? DerniereSortie,
      DateTime? AvantDerniereSortie,
      DateTime? AvantAvantDerniereSortie,
      DateTime? QuatriemeDerniereSortie);

    private sealed record StarPairStatistic(
      string Couple,
      int NombreDeSorties,
      double PourcentageDeSortie,
      DateTime? DerniereSortie,
      DateTime? AvantDerniereSortie,
      DateTime? AvantAvantDerniereSortie,
      DateTime? QuatriemeDerniereSortie);
  }
}
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using Line = System.Windows.Shapes.Line;
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
    private IReadOnlyList<WeightFrequency> weightFrequencies = [];
    private IReadOnlyList<WeightFrequency> starWeightFrequencies = [];

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
      var drawWeights = BuildDrawWeightsTable(distinctFiles);
      PoidsBoules.ItemsSource = drawWeights.DefaultView;
      weightFrequencies = drawWeights.Rows
          .Cast<DataRow>()
          .GroupBy(row => (int)row["PoidsDesBoules"])
          .OrderBy(group => group.Key)
          .Select(group => new WeightFrequency(group.Key, group.Count()))
          .ToArray();
      PoidsBoulesStatistiques.ItemsSource = weightFrequencies;
      starWeightFrequencies = drawWeights.Rows
          .Cast<DataRow>()
          .GroupBy(row => (int)row["PoidsDesEtoiles"])
          .OrderBy(group => group.Key)
          .Select(group => new WeightFrequency(group.Key, group.Count()))
          .ToArray();
      DrawWeightChart();
      DrawStarWeightChart();
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

    private static DataTable BuildDrawWeightsTable(IEnumerable<CsvFileData> files)
    {
      var weights = new DataTable();
      weights.Columns.Add("Tirage", typeof(string));
      weights.Columns.Add("PoidsDesBoules", typeof(int));
      weights.Columns.Add("PoidsDesEtoiles", typeof(int));

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
        if (ballColumns.Length != 5 || starColumns.Length != 2)
        {
          continue;
        }

        foreach (var fields in file.Rows)
        {
          var balls = ReadDrawNumbers(fields, ballColumns, 1, 50);
          var stars = ReadDrawNumbers(fields, starColumns, 1, 12);
          if (balls.Count != 5 || stars.Count != 2)
          {
            continue;
          }

          var draw = string.Join(
              "-",
              ballColumns.Concat(starColumns).Select(column => fields[column]));

          weights.Rows.Add(draw, balls.Sum(), stars.Sum());
        }
      }

      return weights;
    }

    private static HashSet<int> ReadDrawNumbers(string[] fields, IEnumerable<int> columns, int minimum, int maximum)
    {
      var numbers = new HashSet<int>();
      foreach (var column in columns)
      {
        if (column < fields.Length
            && int.TryParse(fields[column], out var number)
            && number >= minimum
            && number <= maximum)
        {
          numbers.Add(number);
        }
      }

      return numbers;
    }

    private void GraphiquePoidsCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
      DrawWeightChart();
    }

    private void DrawWeightChart()
    {
      DrawWeightFrequencyChart(GraphiquePoidsCanvas, weightFrequencies, "Poids des boules");
    }

    private void GraphiquePoidsEtoilesCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
      DrawStarWeightChart();
    }

    private void DrawStarWeightChart()
    {
      DrawWeightFrequencyChart(GraphiquePoidsEtoilesCanvas, starWeightFrequencies, "Poids des étoiles");
    }

    private void DrawWeightFrequencyChart(Canvas canvas, IReadOnlyList<WeightFrequency> frequencies, string weightLabel)
    {
      if (canvas is null)
      {
        return;
      }

      canvas.Children.Clear();
      var width = canvas.ActualWidth;
      var height = canvas.ActualHeight;
      if (width < 200 || height < 150)
      {
        return;
      }

      if (frequencies.Count == 0)
      {
        AddChartText(canvas, "Aucun tirage à afficher.", width / 2 - 75, height / 2 - 12);
        return;
      }

      const double leftMargin = 60;
      const double rightMargin = 25;
      const double topMargin = 35;
      const double bottomMargin = 55;
      var plotWidth = width - leftMargin - rightMargin;
      var plotHeight = height - topMargin - bottomMargin;
      var minimumWeight = (int)Math.Floor(frequencies[0].Weight / 10d) * 10;
      var maximumWeight = (int)Math.Ceiling(frequencies[^1].Weight / 10d) * 10;
      if (maximumWeight == minimumWeight)
      {
        maximumWeight += 10;
      }

      var maximumCount = frequencies.Max(frequency => frequency.Count);
      var countStep = Math.Max(1, (int)Math.Ceiling(maximumCount / 5d));
      var yAxisMaximum = (int)Math.Ceiling(maximumCount / (double)countStep) * countStep;

      AddChartText(canvas, "Quantité de tirages", leftMargin, 8);
      AddChartText(canvas, weightLabel, leftMargin + plotWidth / 2 - 45, height - 25);

      for (var count = 0; count <= yAxisMaximum; count += countStep)
      {
        var y = topMargin + plotHeight - count * plotHeight / yAxisMaximum;
        AddChartLine(canvas, leftMargin, y, leftMargin + plotWidth, y, count == 0 ? Brushes.Black : Brushes.LightGray, count == 0 ? 1.5 : 1);
        AddChartText(canvas, count.ToString(CultureInfo.InvariantCulture), 18, y - 9, 36, TextAlignment.Right);
      }

      const int xTickCount = 5;
      for (var tick = 0; tick <= xTickCount; tick++)
      {
        var weight = minimumWeight + (maximumWeight - minimumWeight) * tick / xTickCount;
        var x = leftMargin + plotWidth * tick / xTickCount;
        AddChartLine(canvas, x, topMargin, x, topMargin + plotHeight, Brushes.LightGray, 1);
        AddChartText(canvas, weight.ToString(CultureInfo.InvariantCulture), x - 20, topMargin + plotHeight + 8, 40, TextAlignment.Center);
      }

      foreach (var frequency in frequencies)
      {
        var x = leftMargin + (frequency.Weight - minimumWeight) * plotWidth / (maximumWeight - minimumWeight);
        var y = topMargin + plotHeight - frequency.Count * plotHeight / yAxisMaximum;
        var point = new Ellipse
        {
          Width = 8,
          Height = 8,
          Fill = Brushes.DodgerBlue,
          Stroke = Brushes.DarkBlue,
          StrokeThickness = 1,
          ToolTip = $"Poids : {frequency.Weight}{Environment.NewLine}Tirages : {frequency.Count}"
        };
        Canvas.SetLeft(point, x - point.Width / 2);
        Canvas.SetTop(point, y - point.Height / 2);
        canvas.Children.Add(point);
      }
    }

    private static void AddChartLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush color, double thickness)
    {
      var line = new Line
      {
        X1 = x1,
        Y1 = y1,
        X2 = x2,
        Y2 = y2,
        Stroke = color,
        StrokeThickness = thickness
      };
      canvas.Children.Add(line);
    }

    private static void AddChartText(Canvas canvas, string text, double left, double top, double width = 150, TextAlignment alignment = TextAlignment.Left)
    {
      var label = new TextBlock
      {
        Text = text,
        Width = width,
        TextAlignment = alignment,
        Foreground = Brushes.Black
      };
      Canvas.SetLeft(label, left);
      Canvas.SetTop(label, top);
      canvas.Children.Add(label);
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

    private sealed record WeightFrequency(int Weight, int Count);

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
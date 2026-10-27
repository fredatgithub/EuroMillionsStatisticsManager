using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualBasic.FileIO;
using Ellipse = System.Windows.Shapes.Ellipse;
using Line = System.Windows.Shapes.Line;

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
    private HashSet<string> drawnBallCombinations = new(StringComparer.Ordinal);
    private Dictionary<string, int> ballSubsetFrequencies = new(StringComparer.Ordinal);
    private readonly HashSet<int> selectedBalls = [];
    private readonly HashSet<int> selectedStars = [];
    private bool estimationHistoryAvailable;

    public MainWindow()
    {
      InitializeComponent();
      InitializeEstimationControls();
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
    }

    private void InitializeEstimationControls()
    {
      for (var number = 1; number <= 50; number++)
      {
        var button = CreateEstimationNumberButton(number);
        button.Click += BallNumber_Click;
        BallNumberPanel.Children.Add(button);
      }

      for (var number = 1; number <= 12; number++)
      {
        var button = CreateEstimationNumberButton(number);
        button.Click += StarNumber_Click;
        StarNumberPanel.Children.Add(button);
      }

      UpdateEstimationDisplay();
    }

    private static int[] GetRandomNumbers(int maximum, int count)
    {
      var numbers = new HashSet<int>();
      while (numbers.Count < count)
      {
        numbers.Add(Random.Shared.Next(1, maximum + 1));
      }

      return numbers.Order().ToArray();
    }

    private static Button CreateEstimationNumberButton(int number)
    {
      return new Button
      {
        Content = number.ToString(CultureInfo.InvariantCulture),
        Tag = number,
        MinWidth = 30,
        Padding = new Thickness(2, 1, 2, 1),
        Margin = new Thickness(2),
        FontWeight = FontWeights.Bold,
        Background = Brushes.White,
        BorderBrush = Brushes.LightGray
      };
    }

    private void BallNumber_Click(object sender, RoutedEventArgs e)
    {
      ToggleNumber(sender, selectedBalls, 5, "boules");
    }

    private void StarNumber_Click(object sender, RoutedEventArgs e)
    {
      ToggleNumber(sender, selectedStars, 2, "étoiles");
    }

    private void ToggleNumber(object sender, HashSet<int> selection, int maximum, string numberType)
    {
      if (sender is not Button { Tag: int number })
      {
        return;
      }

      if (!selection.Remove(number) && selection.Count < maximum)
      {
        selection.Add(number);
      }
      else if (selection.Count >= maximum && !selection.Contains(number))
      {
        EstimationStatus.Text = $"Vous ne pouvez sélectionner que {maximum} {numberType}.";
        return;
      }

      EstimationStatus.Text = string.Empty;
      OptimizedScoreResult.Text = string.Empty;
      UpdateEstimationDisplay();
    }

    private void GenerateDraw_Click(object sender, RoutedEventArgs e)
    {
      SetEstimationSelection(
          GetRandomNumbers(50, 5),
          GetRandomNumbers(12, 2));
      EstimationStatus.Text = "Combinaison générée au hasard.";
      OptimizedScoreResult.Text = string.Empty;
      UpdateEstimationDisplay();
    }

    private void GenerateOptimizedDraw_Click(object sender, RoutedEventArgs e)
    {
      if (!estimationHistoryAvailable)
      {
        EstimationStatus.Text = "L’historique n’est pas chargé ; la génération optimisée est indisponible.";
        return;
      }

      const int candidateCount = 30000;
      var bestScore = long.MinValue;
      var bestCandidates = new List<int[]>();
      var examinedCandidates = new HashSet<string>(StringComparer.Ordinal);

      for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
      {
        var balls = GetRandomNumbers(50, 5);
        var key = FormatNumberCombination(balls);
        if (!examinedCandidates.Add(key) || drawnBallCombinations.Contains(key))
        {
          continue;
        }

        var score = GetHistoricalSubsetScore(balls);
        if (score > bestScore)
        {
          bestScore = score;
          bestCandidates.Clear();
          bestCandidates.Add(balls);
        }
        else if (score == bestScore)
        {
          bestCandidates.Add(balls);
        }
      }

      if (bestCandidates.Count == 0)
      {
        EstimationStatus.Text = "Aucune combinaison inédite trouvée. Vérifiez l’historique chargé.";
        return;
      }

      var chosenBalls = bestCandidates[Random.Shared.Next(bestCandidates.Count)];
      var chosenStars = GetRandomNumbers(12, 2);
      SetEstimationSelection(chosenBalls, chosenStars);
      EstimationStatus.Text = "Combinaison inédite optimisée selon les fréquences historiques des paires, triplets et quadruplets (30 000 essais).";
      OptimizedScoreResult.Text = $"Score historique des sous-combinaisons : {bestScore}. L’historique ne permet pas de prédire les prochains tirages.";
      UpdateEstimationDisplay();
    }

    private void SetEstimationSelection(IEnumerable<int> balls, IEnumerable<int> stars)
    {
      selectedBalls.Clear();
      selectedBalls.UnionWith(balls);
      selectedStars.Clear();
      selectedStars.UnionWith(stars);
    }

    private void UpdateEstimationDisplay()
    {
      BallSelectionCount.Text = $"{selectedBalls.Count} boule(s) sur 5 sélectionnée(s)";
      StarSelectionCount.Text = $"{selectedStars.Count} étoile(s) sur 2 sélectionnée(s)";
      foreach (var button in BallNumberPanel.Children.OfType<Button>())
      {
        UpdateNumberButton(button, selectedBalls.Contains((int)button.Tag));
      }

      foreach (var button in StarNumberPanel.Children.OfType<Button>())
      {
        UpdateNumberButton(button, selectedStars.Contains((int)button.Tag));
      }

      if (selectedBalls.Count != 5 || selectedStars.Count != 2)
      {
        BallWeightResult.Text = string.Empty;
        StarWeightResult.Text = string.Empty;
        BallDistributionResult.Text = string.Empty;
        HistoryMatchResult.Text = string.Empty;
        return;
      }

      var balls = selectedBalls.Order().ToArray();
      var stars = selectedStars.Order().ToArray();
      BallWeightResult.Text = $"Poids des boules : {balls.Sum()} (entre 15 et 240 ; moyenne théorique : 127,5)";
      StarWeightResult.Text = $"Poids des étoiles : {stars.Sum()} (entre 3 et 23 ; moyenne théorique : 13)";
      var rowCounts = Enumerable.Range(0, 5)
          .Select(row => balls.Count(number => (number - 1) / 10 == row));
      var columnCounts = Enumerable.Range(0, 10)
          .Select(column => balls.Count(number => (number - 1) % 10 == column));
      BallDistributionResult.Text =
          $"Nombre par ligne : {string.Join(" + ", rowCounts)}{Environment.NewLine}" +
          $"Nombre par colonne : {string.Join(" + ", columnCounts)}";

      var key = FormatNumberCombination(balls);
      var pairCount = CountHistoricalSubsets(balls, 2);
      var tripletCount = CountHistoricalSubsets(balls, 3);
      var quadrupletCount = CountHistoricalSubsets(balls, 4);
      HistoryMatchResult.Text =
          $"5 boules déjà sorties ensemble : {(drawnBallCombinations.Contains(key) ? "Oui" : "Non")}{Environment.NewLine}" +
          $"Sous-combinaisons déjà vues : paires {pairCount}/10, triplets {tripletCount}/10, quadruplets {quadrupletCount}/5";
    }

    private static void UpdateNumberButton(Button button, bool isSelected)
    {
      button.Background = isSelected ? Brushes.Tomato : Brushes.White;
      button.BorderBrush = isSelected ? Brushes.OrangeRed : Brushes.LightGray;
    }

    private long GetHistoricalSubsetScore(int[] balls)
    {
      long score = 0;
      for (var subsetSize = 2; subsetSize <= 4; subsetSize++)
      {
        score += GetSubsetFrequencyScore(balls, subsetSize);
      }

      return score;
    }

    private long GetSubsetFrequencyScore(int[] numbers, int subsetSize)
    {
      long score = 0;
      VisitCombinations(numbers, subsetSize, combination =>
      {
        if (ballSubsetFrequencies.TryGetValue(FormatNumberCombination(combination), out var count))
        {
          score += count;
        }
      });
      return score;
    }

    private int CountHistoricalSubsets(int[] numbers, int subsetSize)
    {
      var count = 0;
      VisitCombinations(numbers, subsetSize, combination =>
      {
        if (ballSubsetFrequencies.ContainsKey(FormatNumberCombination(combination)))
        {
          count++;
        }
      });
      return count;
    }

    private static void VisitCombinations(int[] numbers, int subsetSize, Action<int[]> action)
    {
      var combination = new int[subsetSize];

      void Visit(int sourceIndex, int combinationIndex)
      {
        if (combinationIndex == subsetSize)
        {
          action(combination);
          return;
        }

        for (var index = sourceIndex; index <= numbers.Length - (subsetSize - combinationIndex); index++)
        {
          combination[combinationIndex] = numbers[index];
          Visit(index + 1, combinationIndex + 1);
        }
      }

      Visit(0, 0);
    }

    private static string FormatNumberCombination(IEnumerable<int> numbers)
    {
      return string.Join("-", numbers);
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

    public async Task LoadHistoryAsync()
    {
      LogAction("Chargement des fichiers CSV du dossier Archives.");
      var result = await Task.Run(PrepareHistoryData);
      foreach (var message in result.LogMessages)
      {
        LogAction(message);
      }

      if (result.ArchiveError is not null)
      {
        ShowArchiveError(result.ArchiveError);
        return;
      }

      Historique.ItemsSource = result.History.DefaultView;
      drawnBallCombinations = result.EstimationHistory.DrawnBallCombinations;
      ballSubsetFrequencies = result.EstimationHistory.SubsetFrequencies;
      estimationHistoryAvailable = true;
      PoidsBoules.ItemsSource = result.DrawWeights.DefaultView;
      weightFrequencies = result.BallWeightFrequencies;
      PoidsBoulesStatistiques.ItemsSource = weightFrequencies;
      starWeightFrequencies = result.StarWeightFrequencies;
      PoidsEtoilesStatistiques.ItemsSource = starWeightFrequencies;
      DrawWeightChart();
      DrawStarWeightChart();
      Statistiques.ItemsSource = result.BallStatistics;
      StatistiquesPairesBoules.ItemsSource = result.BallPairStatistics;
      StatistiquesTripletsBoules.ItemsSource = result.BallTripletStatistics;
      StatistiquesQuadrupletsBoules.ItemsSource = result.BallQuadrupletStatistics;
      StatistiquesQuintupletsBoules.ItemsSource = result.BallQuintupletStatistics;
      StatistiquesEtoiles.ItemsSource = result.StarStatistics;
      StatistiquesPairesEtoiles.ItemsSource = result.StarPairStatistics;
      StatusText.Text = $"{result.History.Rows.Count} tirage(s) chargé(s) depuis {result.ImportedFileCount} fichier(s).";
      if (result.DuplicateCount > 0)
      {
        StatusText.Text += $" {result.DuplicateCount} doublon(s) ignoré(s).";
        LogAction($"{result.DuplicateCount} doublon(s) ignoré(s).");
      }

      LogAction($"{result.History.Rows.Count} tirage(s) chargé(s) depuis {result.ImportedFileCount} fichier(s) CSV.");
      if (result.Errors.Count > 0)
      {
        StatusText.Text += $" {result.Errors.Count} fichier(s) n’ont pas pu être importés.";
        LogAction($"Chargement terminé avec {result.Errors.Count} erreur(s).");
        MessageBox.Show(
            string.Join(Environment.NewLine, result.Errors),
            "Erreurs de chargement des archives",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
      }
    }

    private HistoryLoadResult PrepareHistoryData()
    {
      var logMessages = new List<string>();
      try
      {
        Directory.CreateDirectory(archivesDirectory);
      }
      catch (IOException exception)
      {
        logMessages.Add($"Échec du chargement des archives : {exception.Message}");
        return HistoryLoadResult.Failed(exception.Message, logMessages);
      }
      catch (UnauthorizedAccessException exception)
      {
        logMessages.Add($"Échec du chargement des archives : {exception.Message}");
        return HistoryLoadResult.Failed(exception.Message, logMessages);
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
        logMessages.Add($"Impossible de lister les fichiers CSV : {exception.Message}");
        return HistoryLoadResult.Failed(exception.Message, logMessages);
      }
      catch (UnauthorizedAccessException exception)
      {
        logMessages.Add($"Impossible de lister les fichiers CSV : {exception.Message}");
        return HistoryLoadResult.Failed(exception.Message, logMessages);
      }

      foreach (var filePath in files)
      {
        try
        {
          importedFiles.Add(ReadCsvFile(filePath));
          logMessages.Add($"Fichier chargé : {Path.GetFileName(filePath)}.");
        }
        catch (IOException exception)
        {
          errors.Add($"{Path.GetFileName(filePath)} : {exception.Message}");
          logMessages.Add($"Erreur de lecture du fichier {Path.GetFileName(filePath)} : {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
          errors.Add($"{Path.GetFileName(filePath)} : {exception.Message}");
          logMessages.Add($"Accès refusé au fichier {Path.GetFileName(filePath)} : {exception.Message}");
        }
        catch (MalformedLineException exception)
        {
          errors.Add($"{Path.GetFileName(filePath)}, ligne {exception.LineNumber} : {exception.Message}");
          logMessages.Add($"Ligne mal formée dans {Path.GetFileName(filePath)}, ligne {exception.LineNumber} : {exception.Message}");
        }
      }

      var (distinctFiles, duplicateCount) = RemoveDuplicateDraws(importedFiles);
      var estimationHistory = BuildEstimationHistory(distinctFiles);
      var history = BuildHistoryTable(distinctFiles);
      var drawWeights = BuildDrawWeightsTable(distinctFiles);
      return new HistoryLoadResult(
          history,
          drawWeights,
          estimationHistory,
          BuildWeightFrequencies(drawWeights, "PoidsDesBoules"),
          BuildWeightFrequencies(drawWeights, "PoidsDesEtoiles"),
          BuildBallStatistics(distinctFiles),
          BuildBallPairStatistics(distinctFiles),
          BuildBallTripletStatistics(distinctFiles),
          BuildBallQuadrupletStatistics(distinctFiles),
          BuildBallQuintupletStatistics(distinctFiles),
          BuildStarStatistics(distinctFiles),
          BuildStarPairStatistics(distinctFiles),
          importedFiles.Count,
          duplicateCount,
          errors,
          logMessages,
          null);
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

    private static EstimationHistory BuildEstimationHistory(IEnumerable<CsvFileData> files)
    {
      var drawnBallCombinations = new HashSet<string>(StringComparer.Ordinal);
      var subsetFrequencies = new Dictionary<string, int>(StringComparer.Ordinal);

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

        foreach (var fields in file.Rows)
        {
          var balls = ReadDrawNumbers(fields, ballColumns, 1, 50).Order().ToArray();
          if (balls.Length != 5)
          {
            continue;
          }

          drawnBallCombinations.Add(FormatNumberCombination(balls));
          for (var subsetSize = 2; subsetSize <= 4; subsetSize++)
          {
            VisitCombinations(balls, subsetSize, combination =>
            {
              var key = FormatNumberCombination(combination);
              subsetFrequencies.TryGetValue(key, out var count);
              subsetFrequencies[key] = count + 1;
            });
          }
        }
      }

      return new EstimationHistory(drawnBallCombinations, subsetFrequencies);
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
      weights.Columns.Add("DateTirage", typeof(DateTime));

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
          var drawDate = dateColumn >= 0 && dateColumn < fields.Length
              ? ParseDrawDate(fields[dateColumn])
              : null;

          weights.Rows.Add(
              draw,
              balls.Sum(),
              stars.Sum(),
              drawDate.HasValue ? (object)drawDate.Value : DBNull.Value);
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

    private static IReadOnlyList<WeightFrequency> BuildWeightFrequencies(DataTable drawWeights, string weightColumn)
    {
      return drawWeights.Rows
          .Cast<DataRow>()
          .GroupBy(row => (int)row[weightColumn])
          .OrderBy(group => group.Key)
          .Select(group =>
          {
            var recentDates = group
                .Select(row => row.Field<DateTime?>("DateTirage"))
                .Where(date => date.HasValue)
                .Select(date => date!.Value)
                .Distinct()
                .OrderByDescending(date => date)
                .Take(2)
                .ToArray();

            return new WeightFrequency(
                group.Key,
                group.Count(),
                recentDates.Length > 0 ? recentDates[0] : null,
                recentDates.Length > 1 ? recentDates[1] : null);
          })
          .ToArray();
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

      const int xTickCount = 20;
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

    private sealed record EstimationHistory(
      HashSet<string> DrawnBallCombinations,
      Dictionary<string, int> SubsetFrequencies)
    {
      public static EstimationHistory Empty { get; } = new(
          new HashSet<string>(StringComparer.Ordinal),
          new Dictionary<string, int>(StringComparer.Ordinal));
    }

    private sealed record HistoryLoadResult(
      DataTable History,
      DataTable DrawWeights,
      EstimationHistory EstimationHistory,
      IReadOnlyList<WeightFrequency> BallWeightFrequencies,
      IReadOnlyList<WeightFrequency> StarWeightFrequencies,
      IReadOnlyList<BallStatistic> BallStatistics,
      IReadOnlyList<BallPairStatistic> BallPairStatistics,
      IReadOnlyList<BallTripletStatistic> BallTripletStatistics,
      IReadOnlyList<BallQuadrupletStatistic> BallQuadrupletStatistics,
      IReadOnlyList<BallQuintupletStatistic> BallQuintupletStatistics,
      IReadOnlyList<StarStatistic> StarStatistics,
      IReadOnlyList<StarPairStatistic> StarPairStatistics,
      int ImportedFileCount,
      int DuplicateCount,
      IReadOnlyList<string> Errors,
      IReadOnlyList<string> LogMessages,
      string? ArchiveError)
    {
      public static HistoryLoadResult Failed(string message, IReadOnlyList<string> logMessages)
      {
        return new HistoryLoadResult(
            new DataTable(),
            new DataTable(),
            EstimationHistory.Empty,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            0,
            0,
            [],
            logMessages,
            message);
      }
    }

    private sealed record WeightFrequency(int Weight, int Count, DateTime? Latest, DateTime? Previous);

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
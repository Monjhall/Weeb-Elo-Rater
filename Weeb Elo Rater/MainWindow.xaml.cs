using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;

namespace Weeb_Elo_Rater
{

    public enum SeriesType
    {
        TV = 0,
        MOVIE = 1,
        OVA = 2,
        ONA = 3,
        SPECIAL = 4
    }

    public enum StatusType
    {
        WATCHING = 0,
        COMPLETED = 1,
        ON_HOLD = 2,
        PLAN_TO_WATCH = 3,
        DROPPED = 4
    }

    /// <summary>
    /// Class for storing anime information.
    /// </summary>
    public class Anime
    {
        public string Title { get; set; }
        public SeriesType Series { get; set; }
        public StatusType Status { get; set; }
        public int Elo { get; set; } = 1500;
        public double ActualScore { get; set; }
        public double ExpectedScore { get; set; }
        public string AnimeId { get; set; }
        public string ImageLink { get; set; } = "https://cdn.myanimelist.net/images/favicon.ico";

        // Method that overrides the base class (System.Object) implementation.
        public override string ToString()
        {
            return Title;
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// </summary>
    public partial class MainWindow : Window
    {
        // List of all the animes imported from MAL.
        ObservableCollection<Anime> TotalAnimeList = new ObservableCollection<Anime>();

        // List of anime to be displayed for ranking purposes.
        List<ObservableCollection<Anime>> LobbyList = new List<ObservableCollection<Anime>>();

        // Integer that tracks the current round of ranking.
        int RoundNumber = 0;

        // Integer that tracks the current Lobby within a round.
        int LobbyNumber = 0;

        // TODO: MAKE A CONFIGURABLE PROPERTY
        const int AnimePerLobby = 4;

        // Boolean that tracks whether the ranking has started.
        bool RankingInProgress = false;

        // Dictionary that maps series type XML value to the enumeration.
        Dictionary<string, SeriesType> SeriesTypeMap = new Dictionary<string, SeriesType> {
            { "TV", SeriesType.TV },
            { "Movie", SeriesType.MOVIE },
            { "OVA", SeriesType.OVA },
            { "ONA", SeriesType.ONA },
            { "Special", SeriesType.SPECIAL }
        };

        // Dictionary that maps status type XML value to the enumeration.
        Dictionary<string, StatusType> StatusTypeMap = new Dictionary<string, StatusType> {
            { "Watching", StatusType.WATCHING },
            { "Completed", StatusType.COMPLETED },
            { "On-Hold", StatusType.ON_HOLD },
            { "Plan to Watch", StatusType.PLAN_TO_WATCH },
            { "Dropped", StatusType.DROPPED }
        };

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            // Configure the listbox to allow sorting.
            Style AnimeListBoxStyle = new Style(typeof(ListBoxItem));
            AnimeListBoxStyle.Setters.Add(new Setter(AllowDropProperty, true));
            AnimeListBoxStyle.Setters.Add(new EventSetter(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(AnimeListBoxPreviewMouseLeftButtonDown)));
            AnimeListBoxStyle.Setters.Add(new EventSetter(DropEvent, new DragEventHandler(AnimeListBoxDrop)));
            AnimeListBox.ItemContainerStyle = AnimeListBoxStyle;
        }

        private void LoadButtonClick(object Sender, RoutedEventArgs Args)
        {
            // Open a user-specified XML file.
            OpenFileDialog LoadFileDialog = new OpenFileDialog();
            LoadFileDialog.Filter = "XML Files | *.xml";

            if (LoadFileDialog.ShowDialog() == true)
            {
                // Load the XML document.
                XDocument XmlDocument = XDocument.Load(LoadFileDialog.FileName);

                // Check if this export is from MAL.
                if (XmlDocument.Root.Name != "myanimelist")
                {
                    MessageBox.Show("XML provided is not an export from MAL!", "XML Read Error", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);
                    return;
                }

                // Clear the existing list of anime.
                TotalAnimeList.Clear();

                // Process information for each anime.
                foreach (XElement Element in XmlDocument.Root.Elements())
                {

                    // Post the list information.
                    if (Element.Name.LocalName == "myinfo")
                    {
                        ListInformationBlock.Text = "List Information:" + "\n";

                        foreach (XElement InfomationElement in Element.Elements())
                        {
                            string ReadableName = ElementNameToString(InfomationElement.Name.LocalName);

                            if (ReadableName != "")
                            {
                                ListInformationBlock.Text += ReadableName + ": " + InfomationElement.Value + "\n";
                            }
                        }
                    }

                    // Store any animes in the sortable anime list.
                    if (Element.Name.LocalName == "anime")
                    {
                        Anime AnimeEntry = new Anime();

                        ProcessXmlAnime(Element, AnimeEntry);

                        TotalAnimeList.Add(AnimeEntry);
                    }
                }

                // Allow for ranking if there are at least 10 animes.
                StartStopButton.IsEnabled = (TotalAnimeList.Count >= 10);

                // Show all the anime from the list in the listbox.
                AnimeListBox.ItemsSource = TotalAnimeList;
            }
        }

        private async void ProcessXmlAnime(XElement AnimeElement, Anime AnimeEntry)
        {
            foreach (XElement AnimeFeature in AnimeElement.Elements())
            {
                if (AnimeFeature.Name.LocalName == "series_animedb_id")
                {
                    AnimeEntry.AnimeId = AnimeFeature.Value;
                }

                if (AnimeFeature.Name.LocalName == "series_title")
                {
                    AnimeEntry.Title = AnimeFeature.Value;
                }

                if (AnimeFeature.Name.LocalName == "series_type")
                {
                    AnimeEntry.Series = SeriesTypeMap[AnimeFeature.Value];
                }

                if (AnimeFeature.Name.LocalName == "my_status")
                {
                    AnimeEntry.Status = StatusTypeMap[AnimeFeature.Value];
                }
            }

            // Create a lamba task to asynchronously gather anime images.
            Task GetImageTask = Task.Run(() => AnimeEntry.ImageLink = GetAnimeImage(GetMalHtml(AnimeEntry.AnimeId)));
            await GetImageTask;
        }

        private void StartStopButtonClick(object Sender, RoutedEventArgs Args)
        {
            // If ranking is in progress, stop it.
            if (RankingInProgress)
            {
                // Sort the total anime list by Elo rating and show the new list.
                TotalAnimeList = new ObservableCollection<Anime>(TotalAnimeList.OrderByDescending(X => X.Elo).ToList());
                AnimeListBox.ItemsSource = TotalAnimeList;

                // Disable the next and previous lobby buttons.
                NextLobbyButton.IsEnabled = false;
                PreviousLobbyButton.IsEnabled = false;

                // Update the lobby title.
                LobbyTitle.Text = "Ranking Complete!";

                // Update button text.
                StartStopButton.Content = "Start Ranking Anime";
            }

            // Start the ranking process.
            else
            {
                // Create a subset of the anime to rank.
                ObservableCollection<Anime> RemoveList = new ObservableCollection<Anime>();

                foreach (Anime AnimeEntry in TotalAnimeList)
                {
                    if (AnimeEntry.Status != StatusType.COMPLETED)
                    {
                        RemoveList.Add(AnimeEntry);
                    }
                }

                foreach (Anime AnimeToRemove in RemoveList)
                {
                    TotalAnimeList.Remove(AnimeToRemove);
                }

                // Enabled the next and previous lobby buttons.
                NextLobbyButton.IsEnabled = true;
                PreviousLobbyButton.IsEnabled = true;

                // Update the lobby number, round number, and lobby title.
                LobbyNumber = 0;
                RoundNumber = 0;
                LobbyTitle.Text = "Round: " + RoundNumber + "\n" + "Lobby: " + LobbyNumber;

                // Update button text.
                StartStopButton.Content = "Stop Ranking Anime";

                // Start the next lobby.
                CreateLobbyList();

                UpdateCurrentLobby();
            }

            // Flip the ranking in progress flag.
            RankingInProgress = !RankingInProgress;
        }

        private string ElementNameToString(string ElementName)
        {
            string ReadableName = "";

            if (ElementName == "user_name")
            {
                ReadableName = "Username";
            }

            if (ElementName == "user_total_watching")
            {
                ReadableName = "Watching";
            }

            if (ElementName == "user_total_completed")
            {
                ReadableName = "Completed";
            }

            if (ElementName == "user_total_onhold")
            {
                ReadableName = "On Hold";
            }

            if (ElementName == "user_total_dropped")
            {
                ReadableName = "Dropped";
            }

            if (ElementName == "user_total_plantowatch")
            {
                ReadableName = "Plan to Watch";
            }

            return ReadableName;
        }

        private void AnimeListBoxPreviewMouseLeftButtonDown(object Sender, MouseButtonEventArgs Args)
        {
            if (Sender is ListBoxItem)
            {
                ListBoxItem DraggedItem = Sender as ListBoxItem;
                DragDrop.DoDragDrop(DraggedItem, DraggedItem.DataContext, DragDropEffects.Move);
                DraggedItem.IsSelected = true;
            }
        }

        private void AnimeListBoxDrop(object Sender, DragEventArgs Args)
        {
            Anime DroppedData = Args.Data.GetData(typeof(Anime)) as Anime;
            Anime Target = ((ListBoxItem)(Sender)).DataContext as Anime;

            int RemovedIndex = AnimeListBox.Items.IndexOf(DroppedData);
            int TargetIndex = AnimeListBox.Items.IndexOf(Target);

            if (RemovedIndex < TargetIndex)
            {
                ((ObservableCollection<Anime>)AnimeListBox.ItemsSource).Insert(TargetIndex + 1, DroppedData);
                ((ObservableCollection<Anime>)AnimeListBox.ItemsSource).RemoveAt(RemovedIndex);
            }
            else
            {
                RemovedIndex++;
                if (((ObservableCollection<Anime>)AnimeListBox.ItemsSource).Count + 1 > RemovedIndex)
                {
                    ((ObservableCollection<Anime>)AnimeListBox.ItemsSource).Insert(TargetIndex, DroppedData);
                    ((ObservableCollection<Anime>)AnimeListBox.ItemsSource).RemoveAt(RemovedIndex);
                }
            }
        }

        // Increments the lobby number and updates the current lobby.
        private void NextLobbyButtonClick(object Sender, RoutedEventArgs Args)
        {
            LobbyNumber++;
            UpdateCurrentLobby();
        }

        // Decrements the lobby number and updates the current lobby.
        private void PreviousLobbyButtonClick(object Sender, RoutedEventArgs Args)
        {            
            LobbyNumber--;
            UpdateCurrentLobby();
        }

        private void NextRoundButtonClick(object Sender, RoutedEventArgs Args)
        {
            // Calculate the change Elo for each anime.
            foreach(ObservableCollection<Anime> Lobby in LobbyList)
            {
                foreach(Anime AnimeEntry in Lobby)
                {
                    AnimeEntry.ActualScore = Lobby.Count - (Lobby.IndexOf(AnimeEntry) + 1);
                    AnimeEntry.Elo = CalculateChangeInRating(AnimeEntry);
                }
            }

            // Create the next lobby and update the current lobby.
            CreateLobbyList();
            UpdateCurrentLobby();
        }

        private List<int> GenerateRandomIndexList(int AnimeCount, int IndexCount)
        {
            Random RandomGenerator = new Random();

            List<int> IndexList = new List<int>();

            // Generate random indices and add them to the list if the list doesn't contain them.
            while(IndexList.Count < IndexCount)
            {
                int Index = RandomGenerator.Next(0, AnimeCount);

                if (!IndexList.Contains(Index))
                {
                    IndexList.Add(Index);
                }
            }

            return IndexList;
        }

        private double CalculateIndividualExpectedScore(int FirstElo, int SecondElo)
        {
            double Exponent = (SecondElo - FirstElo) / 400.0;
            double Result = 1.0 / (1.0 + Math.Pow(10.0, Exponent));
            return Result;
        }

        private void CreateLobbyList()
        {
            // Shuffle the total anime list.
            Random RandomGenerator = new Random();
            TotalAnimeList = new ObservableCollection<Anime>(TotalAnimeList.OrderBy(R => RandomGenerator.Next()).ToList());

            // Randomly generate the lobbies needed for ranking.
            LobbyList.Clear();

            // Calculate the total number of lobbies per round.
            int LobbyCount = TotalAnimeList.Count / AnimePerLobby;
            
            // Add lobbies to the lobby list.
            for (int i = 0; i < LobbyCount; i++)
            {
                ObservableCollection<Anime> Lobby = new ObservableCollection<Anime>();

                // Fill the individual lobby with the anime from the total anime list.
                for (int j = 0; j < AnimePerLobby; j++)
                {
                    Lobby.Add(TotalAnimeList[j + (i * AnimePerLobby)]);
                }

                LobbyList.Add(Lobby);
            }

            // Disabled the next round button.
            NextRoundButton.IsEnabled = false;

            // Update the round number and the current lobby number.
            LobbyNumber = 0;
            RoundNumber++;

            // Calculate the expected score of each anime in each lobby.
            foreach (ObservableCollection<Anime> Lobby in LobbyList)
            {
                foreach (Anime AnimeEntry in Lobby)
                {
                    AnimeEntry.ExpectedScore = 0.0;

                    foreach (Anime OtherAnimeEntry in Lobby)
                    {
                        if (AnimeEntry != OtherAnimeEntry)
                        {
                            AnimeEntry.ExpectedScore += CalculateIndividualExpectedScore(AnimeEntry.Elo, OtherAnimeEntry.Elo);
                        }
                    }
                }
            }
        }

        private void UpdateCurrentLobby()
        {
            // Enable or disable the next of previous lobby buttons.
            NextLobbyButton.IsEnabled = LobbyNumber != LobbyList.Count - 1;
            PreviousLobbyButton.IsEnabled = LobbyNumber != 0;

            // If we've reached the end, allow the user to proceed to the next round.
            if (LobbyNumber == LobbyList.Count - 1)
            {
                NextRoundButton.IsEnabled = true;
            }

            // Update the list box item source to the current lobby.
            AnimeListBox.ItemsSource = LobbyList[LobbyNumber];

            // Update the title of the ranking lobby.
            LobbyTitle.Text = "Round: " + (RoundNumber + 1) + "\n" + "Lobby: " + (LobbyNumber + 1);
        }

        private int CalculateChangeInRating(Anime AnimeEntry)
        {
            return AnimeEntry.Elo + (int)(16 * (AnimeEntry.ActualScore - AnimeEntry.ExpectedScore));
        }

        private string GetMalHtml(string AnimeId)
        {
            string MalUrl = "https://myanimelist.net/anime/" + AnimeId;
            string HtmlData = "";

            var Request = (HttpWebRequest)WebRequest.Create(MalUrl);
            Request.UseDefaultCredentials = true;
            var Response = (HttpWebResponse)Request.GetResponse();

            using (Stream HtmlDataStream = Response.GetResponseStream())
            {
                if (HtmlDataStream == null)
                    return "";
                using (var Reader = new StreamReader(HtmlDataStream))
                {
                    HtmlData = Reader.ReadToEnd();
                }
            }
            return HtmlData;
        }

        private string GetAnimeImage(string HtmlData)
        {
            int StartIndex = HtmlData.IndexOf("data-src=", StringComparison.Ordinal);

            StartIndex = StartIndex + 10;
            int EndIndex = HtmlData.IndexOf("\"", StartIndex, StringComparison.Ordinal);
            string ImageUrl = HtmlData.Substring(StartIndex, EndIndex - StartIndex);

            return ImageUrl;
        }
    }
}

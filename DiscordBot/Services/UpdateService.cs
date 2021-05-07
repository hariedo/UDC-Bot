﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using DiscordBot.Extensions;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Services
{
    public class BotData
    {
        public DateTime LastPublisherCheck { get; set; }
        public List<ulong> LastPublisherId { get; set; }
        public DateTime LastUnityDocDatabaseUpdate { get; set; }
    }

    public class UserData
    {
        public Dictionary<ulong, DateTime> MutedUsers { get; set; }
        public Dictionary<ulong, DateTime> ThanksReminderCooldown { get; set; }
        public Dictionary<ulong, DateTime> CodeReminderCooldown { get; set; }

        public UserData()
        {
            MutedUsers = new Dictionary<ulong, DateTime>();
            ThanksReminderCooldown = new Dictionary<ulong, DateTime>();
            CodeReminderCooldown = new Dictionary<ulong, DateTime>();
        }
    }

    public class FaqData
    {
        public string Question { get; set; }
        public string Answer { get; set; }
        public string[] Keywords { get; set; }
    }

    public class FeedData
    {
        public DateTime LastUnityReleaseCheck { get; set; }
        public DateTime LastUnityBlogCheck { get; set; }
        public List<string> PostedIds { get; set; }

        public FeedData()
        {
            PostedIds = new List<string>();
        }
    }

    //TODO Download all avatars to cache them

    public class UpdateService
    {
        DiscordSocketClient _client;
        private readonly ILoggingService _loggingService;
        private readonly PublisherService _publisherService;
        private readonly DatabaseService _databaseService;
        private readonly FeedService _feedService;
        private readonly CancellationToken _token;
        private readonly Settings.Deserialized.Settings _settings;

        private BotData _botData;
        private List<FaqData> _faqData;
        private Random _random;
        private UserData _userData;
        private FeedData _feedData;

        private string[][] _manualDatabase;
        private string[][] _apiDatabase;

        public UpdateService(DiscordSocketClient client, ILoggingService loggingService, PublisherService publisherService,
            DatabaseService databaseService, Settings.Deserialized.Settings settings, FeedService feedService)
        {
            _client = client;
            _loggingService = loggingService;
            _publisherService = publisherService;
            _databaseService = databaseService;
            _feedService = feedService;

            _settings = settings;
            _token = new CancellationToken();
            _random = new Random();

            UpdateLoop();
        }

        private void UpdateLoop()
        {
            ReadDataFromFile();
            SaveDataToFile();
            //CheckDailyPublisher();
            UpdateUserRanks();
            UpdateDocDatabase();
            UpdateRssFeeds();
        }

        private void ReadDataFromFile()
        {
            if (File.Exists($"{_settings.ServerRootPath}/botdata.json"))
            {
                string json = File.ReadAllText($"{_settings.ServerRootPath}/botdata.json");
                _botData = JsonConvert.DeserializeObject<BotData>(json);
            }
            else
                _botData = new BotData();

            if (File.Exists($"{_settings.ServerRootPath}/userdata.json"))
            {
                string json = File.ReadAllText($"{_settings.ServerRootPath}/userdata.json");
                _userData = JsonConvert.DeserializeObject<UserData>(json);

                Task.Run(
                    async () =>
                    {
                        while (_client.ConnectionState != ConnectionState.Connected || _client.LoginState != LoginState.LoggedIn)
                            await Task.Delay(100, _token);
                        await Task.Delay(10000, _token);
                        //Check if there are users still muted
                        foreach (var userID in _userData.MutedUsers)
                        {
                            if (_userData.MutedUsers.HasUser(userID.Key, evenIfCooldownNowOver: true))
                            {
                                SocketGuild guild = _client.Guilds.First(g => g.Id == _settings.guildId);
                                SocketGuildUser sgu = guild.GetUser(userID.Key);
                                if (sgu == null)
                                {
                                    continue;
                                }

                                IGuildUser user = sgu as IGuildUser;

                                IRole mutedRole = user.Guild.GetRole(_settings.MutedRoleId);
                                //Make sure they have the muted role
                                if (!user.RoleIds.Contains(_settings.MutedRoleId))
                                {
                                    await user.AddRoleAsync(mutedRole);
                                }

                                //Setup delay to remove role when time is up.
                                var _ = Task.Run(async () =>
                                {
                                    await _userData.MutedUsers.AwaitCooldown(user.Id);
                                    await user.RemoveRoleAsync(mutedRole);
                                }, _token);
                            }
                        }
                    }, _token);
            }
            else
            {
                _userData = new UserData();
            }

            if (File.Exists($"{_settings.ServerRootPath}/FAQs.json"))
            {
                string json = File.ReadAllText($"{_settings.ServerRootPath}/FAQs.json");
                _faqData = JsonConvert.DeserializeObject<List<FaqData>>(json);
            }
            else
            {
                _faqData = new List<FaqData>();
            }

            if (File.Exists($"{_settings.ServerRootPath}/feeds.json"))
            {
                string json = File.ReadAllText($"{_settings.ServerRootPath}/feeds.json");
                _feedData = JsonConvert.DeserializeObject<FeedData>(json);
            }
            else
            {
                _feedData = new FeedData();
            }
        }


        /*
        ** Save data to file every 20s
        */

        private async void SaveDataToFile()
        {
            while (true)
            {
                var json = JsonConvert.SerializeObject(_botData);
                File.WriteAllText($"{_settings.ServerRootPath}/botdata.json", json);

                json = JsonConvert.SerializeObject(_userData);
                File.WriteAllText($"{_settings.ServerRootPath}/userdata.json", json);

                json = JsonConvert.SerializeObject(_feedData);
                File.WriteAllText($"{_settings.ServerRootPath}/feeds.json", json);
                //await _logging.LogAction("Data successfully saved to file", true, false);
                await Task.Delay(TimeSpan.FromSeconds(20d), _token);
            }
        }

        public async Task CheckDailyPublisher(bool force = false)
        {
            await Task.Delay(TimeSpan.FromSeconds(10d), _token);
            while (true)
            {
                if (_botData.LastPublisherCheck < DateTime.Now - TimeSpan.FromDays(1d) || force)
                {
                    uint count = _databaseService.GetPublisherAdCount();
                    ulong id;
                    uint rand;
                    do
                    {
                        rand = (uint) _random.Next((int) count);
                        id = _databaseService.GetPublisherAd(rand).userId;
                    } while (_botData.LastPublisherId.Contains(id));

                    await _publisherService.PostAd(rand);
                    await _loggingService.LogAction("Posted new daily publisher ad.", true, false);
                    _botData.LastPublisherCheck = DateTime.Now;
                    _botData.LastPublisherId.Add(id);
                }

                if (_botData.LastPublisherId.Count > 10)
                    _botData.LastPublisherId.RemoveAt(0);

                if (force)
                    return;
                await Task.Delay(TimeSpan.FromMinutes(5d), _token);
            }
        }

        private async void UpdateUserRanks()
        {
            await Task.Delay(TimeSpan.FromSeconds(30d), _token);
            while (true)
            {
                _databaseService.UpdateUserRanks();
                await Task.Delay(TimeSpan.FromMinutes(1d), _token);
            }
        }

        public async Task<string[][]> GetManualDatabase()
        {
            if (_manualDatabase == null)
                await LoadDocDatabase();
            return _manualDatabase;
        }

        public async Task<string[][]> GetApiDatabase()
        {
            if (_apiDatabase == null)
                await LoadDocDatabase();
            return _apiDatabase;
        }

        public List<FaqData> GetFaqData()
        {
            return _faqData;
        }

        private async Task LoadDocDatabase()
        {
            if (File.Exists($"{_settings.ServerRootPath}/unitymanual.json") &&
                File.Exists($"{_settings.ServerRootPath}/unityapi.json"))
            {
                string json = File.ReadAllText($"{_settings.ServerRootPath}/unitymanual.json");
                _manualDatabase = JsonConvert.DeserializeObject<string[][]>(json);
                json = File.ReadAllText($"{_settings.ServerRootPath}/unityapi.json");
                _apiDatabase = JsonConvert.DeserializeObject<string[][]>(json);
            }
            else
                await DownloadDocDatabase();
        }

        private async Task DownloadDocDatabase()
        {
            try
            {
                HtmlWeb htmlWeb = new HtmlWeb();
                htmlWeb.CaptureRedirect = true;

                HtmlDocument manual = await htmlWeb.LoadFromWebAsync("https://docs.unity3d.com/Manual/docdata/index.js");
                string manualInput = manual.DocumentNode.OuterHtml;

                HtmlDocument api = await htmlWeb.LoadFromWebAsync("https://docs.unity3d.com/ScriptReference/docdata/index.js");
                string apiInput = api.DocumentNode.OuterHtml;


                _manualDatabase = ConvertJsToArray(manualInput, true);
                _apiDatabase = ConvertJsToArray(apiInput, false);

                File.WriteAllText($"{_settings.ServerRootPath}/unitymanual.json", JsonConvert.SerializeObject(_manualDatabase));
                File.WriteAllText($"{_settings.ServerRootPath}/unityapi.json", JsonConvert.SerializeObject(_apiDatabase));

                string[][] ConvertJsToArray(string data, bool isManual)
                {
                    List<string[]> list = new List<string[]>();
                    string pagesInput;
                    if (isManual)
                    {
                        pagesInput = data.Split("info = [")[0].Split("pages=")[1];
                        pagesInput = pagesInput.Substring(2, pagesInput.Length - 4);
                    }
                    else
                    {
                        pagesInput = data.Split("info =")[0];
                        pagesInput = pagesInput.Substring(63, pagesInput.Length - 65);
                    }


                    foreach (string s in pagesInput.Split("],["))
                    {
                        string[] ps = s.Split(",");
                        list.Add(new string[] {ps[0].Replace("\"", ""), ps[1].Replace("\"", "")});
                        //Console.WriteLine(ps[0].Replace("\"", "") + "," + ps[1].Replace("\"", ""));
                    }

                    return list.ToArray();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private async void UpdateDocDatabase()
        {
            while (true)
            {
                if (_botData.LastUnityDocDatabaseUpdate < DateTime.Now - TimeSpan.FromDays(1d))
                    await DownloadDocDatabase();

                await Task.Delay(TimeSpan.FromHours(1), _token);
            }
        }

        private async void UpdateRssFeeds()
        {
            await Task.Delay(TimeSpan.FromSeconds(30d), _token);
            while (true)
            {
                if (_feedData != null)
                {
                    if (_feedData.LastUnityReleaseCheck < DateTime.Now - TimeSpan.FromMinutes(5))
                    {
                        _feedData.LastUnityReleaseCheck = DateTime.Now;

                        _feedService.CheckUnityBetas(_feedData);
                        _feedService.CheckUnityReleases(_feedData);
                    }

                    if (_feedData.LastUnityBlogCheck < DateTime.Now - TimeSpan.FromMinutes(10))
                    {
                        _feedData.LastUnityBlogCheck = DateTime.Now;

                        _feedService.CheckUnityBlog(_feedData);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(30d), _token);
            }
        }
        /// <summary>
        /// JSON object for the Wikipedia command to convert results to.
        /// </summary>
        private partial class WikiPage
        {
            [JsonProperty("index")]
            public long Index { get; set; }

            [JsonProperty("title")]
            public string Title { get; set; }

            [JsonProperty("extract")]
            public string Extract { get; set; }

            [JsonProperty("fullurl")]
            public Uri FullURL { get; set; }
        }

        public async Task<(string name, string extract, string url)> DownloadWikipediaArticle(string searchQuery)
        {
            string wikiSearchUri = Uri.EscapeUriString(_settings.WikipediaSearchPage + searchQuery);
            HtmlWeb htmlWeb = new HtmlWeb() { CaptureRedirect = true };
            HtmlDocument wikiSearchResponse;

            try { wikiSearchResponse = await htmlWeb.LoadFromWebAsync(wikiSearchUri); }
            catch
            {
                Console.WriteLine("Wikipedia method failed loading URL: " + wikiSearchUri);
                return (null, null, null);
            }
            try
            {
                JObject job = JObject.Parse(wikiSearchResponse.Text);

                if(job.TryGetValue("query", out var query))
                {
                    var pages = JsonConvert.DeserializeObject<List<WikiPage>>(job[query.Path]["pages"].ToString(), new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore });

                    if (pages != null && pages.Count > 0)
                    {
                        pages.Sort((x, y) => x.Index.CompareTo(y.Index)); //Sort from smallest index to biggest, smallest index is indicitive of best matching result
                        var page = pages[0];

                        const string referToString = "may refer to:...";
                        int referToIndex = page.Extract.IndexOf(referToString);
                        //If a multi-refer result was given, reformat title to indicate this and strip the "may refer to" portion from the body
                        if(referToIndex > 0)
                        {
                            int splitIndex = referToIndex + referToString.Length;
                            page.Title = page.Extract.Substring(0, splitIndex - 4); //-4 to strip the useless characters since this will be a title
                            page.Extract = page.Extract.Substring(splitIndex);
                            page.Extract.Replace("\n", Environment.NewLine + "-");
                        }
                        else { page.Extract = page.Extract.Replace("\n", Environment.NewLine); }

                        return (page.Title + ":", page.Extract, page.FullURL.ToString());
                    }
                }
                else { return (null, null, null); }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.WriteLine("Wikipedia method likely failed to parse JSON response from: " + wikiSearchUri);
            }

            return (null, null, null);
        }

        public UserData GetUserData()
        {
            return _userData;
        }

        public void SetUserData(UserData data)
        {
            _userData = data;
        }
    }
}

using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Net;


// XPATH FOR ALL VIDEOS //a[contains(@href,'watch')]
// XPATH FOR NOT VIEWED VIDEOS //a[contains(@href,'watch')]/following::td[1][not(contains(text(),'1'))]

namespace DBBot
{
    class Program
    {
        private static string AccountsUrl = @"https://vex-core.ru/wtf/Accounts.txt";
        private static string Accounts;
        private static string UserChannelId;
        public static string filePath = Directory.GetCurrentDirectory() + "\\config.txt";
        public static string videoToWatchListPath = Directory.GetCurrentDirectory() + "\\videoToWatch.txt";
        public static string watchedVideoPath = Directory.GetCurrentDirectory() + "\\watchedVideo.txt";
        private static UserCredential credential;
        private static string VideoUrl;
        private static string key = "AIzaSyDOasALbFL24OdQAil1g8BnfRKljodlMT4";
        private static string baseVideoListUrl = @"http://hsm.ugatu.su/yt/reports/?cube=1&userId=";
        private static string videoListUrl;
        private static List<string> toWatch = new List<string>();
        private static List<string> watchedVideos = new List<string>();
        private static List<string> sessionVideos = new List<string>();
        private static UserConfig current = new UserConfig();
        private static bool toClose = false;
        private static string[] Comments;
        static async Task Main(string[] args)
        {
            await Run();
            Console.WriteLine("Нажмите любую клавишу...");
            Console.ReadKey();
        }

        // Main logic
        private static async Task Run()
        {
            if (Check())
            {
                Console.WriteLine("Авторизируйтесь через свой Google-аккаунт для просмотра ролика.\n" +
                    "Страница должна автоматически открыться в браузере по умолчанию.");
                // Google authenfication
                await GetOAuth();
                Console.WriteLine("Авторизация прошла успешно.");
                // Getting paid accounts
                await GetAccounts();
                await GetYoutubeAccountId();

                // If account is not in paid list
                if (!CheckLicense())
                {
                    Console.WriteLine("Данная зачетка и выбранный гугл аккаунт отсутствуют в покупателях.\n Номер зачетки: " +
                        current.Id + " ID канала: " + UserChannelId);
                    return;
                }
                // otherwise read comments from file
                ReadComments();
                // If there is no file with watched videos and not all videos watched
                // then getting video list
                Console.WriteLine("Получаем список непросмотренных видео...");
                if (!(File.Exists(videoToWatchListPath) & CheckVideoList()))
                    await GetVideoList();
                await Watch();
            }
        }

        // Checking account in buyers
        private static bool CheckLicense()
        {
            string query = current.Id + ":" + UserChannelId;
            return (Accounts.Contains(query));
        }

        // Getting youtube account id for buyer's list check
        private static async Task GetYoutubeAccountId()
        {
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = key,
                HttpClientInitializer = credential,
                ApplicationName = "YoutubeApp"
            });
            var query = youtubeService.Channels.List("id");
            query.Mine = true;
            var resp = await query.ExecuteAsync();
            if (resp.Items[0] != null & resp.Items[0].Id != String.Empty)
                UserChannelId = resp.Items[0].Id;
        }

        // Getting buyers list
        private static async Task GetAccounts()
        {
            HttpClient client = new HttpClient();
            try
            {
                HttpResponseMessage resp = await client.GetAsync(AccountsUrl);
                Accounts = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }


        private static async Task Watch()
        {
            // Randomizer to emulate user's delay
            Random r = new Random();
            double sec = 0;
            foreach (string video in toWatch)
            {
                // if pressed ESC button then stop watching videos
                if (toClose)
                {
                    break;
                }
                VideoUrl = video;
                // Adding url to watched videos and to session list to delete from file after
                File.AppendAllText(watchedVideoPath, VideoUrl + Environment.NewLine);
                sessionVideos.Add(VideoUrl);
                // Getting video duration in seconds
                sec = await GetVideoDuration(video);
                if (sec == 0)
                {
                    Console.WriteLine("Something is wrong with token. Please restart application.");
                    Environment.Exit(2);
                }
                // Adding delay
                sec += r.Next(30000);
                // Sending a comment
                string id = await SendComment(video);
                // New thread to show time till the end of video
                DateTime now = DateTime.Now;
                now = now.AddMilliseconds(sec);
                Thread timeThread = new Thread(new ParameterizedThreadStart(TimeLeft));
                Thread typeInThread = new Thread(ReadEnd);
                timeThread.Start(now);
                typeInThread.Start();
                Thread.Sleep((int)(sec));
                // Updating comment
                await UpdateComment(id);
            }
            foreach (string s in sessionVideos)
            {
                toWatch.Remove(s);
            }

            WriteVideoList();
            Environment.Exit(0);
        }

        // Reading comments from file
        private static void ReadComments()
        {
            Comments = File.ReadAllLines(Directory.GetCurrentDirectory() + "\\stihi.txt");
        }

        // Getting random comment from file
        private static string GetRandomComment()
        {
            Random r = new Random();
            int numb = r.Next(Comments.Length - 1);
            return Comments[numb] == String.Empty ? GetRandomComment() : Comments[numb];
        }

        // ESC handler
        public static void ReadEnd()
        {
            ConsoleKeyInfo key = Console.ReadKey();
            while (key.Key != ConsoleKey.Escape)
            {
                key = Console.ReadKey();
            }
            toClose = true;
        }

        // Time left till the end of video
        public static void TimeLeft(object Ctime)
        {
            DateTime time = (DateTime)Ctime;
            DateTime currentTime = DateTime.Now;
            TimeSpan interval = time - currentTime;
            while (interval.TotalSeconds > 0)
            {
                Console.Clear();
                currentTime = DateTime.Now;
                interval = time - currentTime;
                if (toClose)
                    Console.WriteLine("Просмотр будет остановлен после просмотра текущего ролика.");
                else
                    Console.WriteLine("Если вы хотите остановить просмотр, то нажмите Esc");
                Console.WriteLine("Смотрим ролик: " + VideoUrl);
                Console.WriteLine("До конца просмотра осталось: " + (int)interval.TotalSeconds + "sec");
                Thread.Sleep(1000);
            };
        }

        // Global check before sending comment
        private static bool Check()
        {
            // Reading id from user if file with config does not exists
            if (!File.Exists(filePath))
            {
                ReadId();
            }
            // Reading config to get id
            ReadFromConfig();
            string input = "3";
            // Optional menu
            while (input != "0")
            {
                Console.WriteLine("Номер зачетной книжки: " + current.Id);
                Console.WriteLine("Для запуска программы нажмите 1.\n" +
                    "Для смены номера зачетной книжки нажмите 2.\n" +
                    "Для смены гугл аккаунта нажмите 3.\n" +
                    "Для выхода нажмите 0.");
                input = Console.ReadLine();
                switch (input)
                {
                    // Start watching
                    case "1":
                        {
                            return true;
                        }
                    // Change user's account
                    case "2":
                        {
                            ReadId();
                            ReadFromConfig();
                            break;
                        }
                    // Change google account
                    case "3":
                        {
                            RemoveGoogleCredentials();
                            break;
                        }
                    // Exit
                    case "0":
                        {
                            Environment.Exit(0);
                            break;
                        }
                }

            }
            return false;
        }

        // Deletes file with google credentials to forget user's google account
        private static void RemoveGoogleCredentials()
        {
            System.IO.DirectoryInfo di = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)+"//ApplicationData");
            foreach (FileInfo file in di.GetFiles())
            {
                file.Delete();
            }
        }


        // Checks if there is any videos left to watch
        private static bool CheckVideoList()
        {
            ReadVideoList();
            if (toWatch.Count > 0)
                return true;
            return false;
        }

        // Writing watched videos to file
        private static void WriteVideoList()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string s in toWatch)
            {
                sb.AppendLine(s);
            }
            File.WriteAllText(videoToWatchListPath, sb.ToString());
        }

        // Reading to watch videos from file
        private static void ReadVideoList()
        {
            toWatch = new List<string>();
            if (File.Exists(videoToWatchListPath))
                using (StreamReader rw = new StreamReader(videoToWatchListPath))
                {
                    while (!rw.EndOfStream)
                    {
                        toWatch.Add(rw.ReadLine());
                    }
                }
        }

        // Reading watched videos to file (not used)
        private static void ReadWatchedVideoList()
        {
            watchedVideos = new List<string>();
            using (StreamReader rw = new StreamReader(watchedVideoPath))
            {
                while (!rw.EndOfStream)
                    watchedVideos.Add(rw.ReadLine());
            }
        }

        // Getting videos to watch from site
        private static async Task GetVideoList()
        {
            HttpClient client = new HttpClient();
            try
            {
                // Getting html with user's videos table
                VideoUrl = baseVideoListUrl + current.Id;
                HttpResponseMessage resp = await client.GetAsync(VideoUrl);
                var responseString = await resp.Content.ReadAsStringAsync();

                // Parsing watched videos
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseString);
                var nodes = doc.DocumentNode.SelectNodes("//a[contains(@href,'watch')]/following::td[5][not(contains(text(),'1'))]");
                var nodesWatched = doc.DocumentNode.SelectNodes("//a[contains(@href,'watch')]/following::td[5][contains(text(),'1')]");
                if (nodes.Count == 0)
                {
                    Console.WriteLine("Все видео просмотрены.");
                    return;
                }
                // Regex to find youtube url 
                Regex regex = new Regex(@"http(?:s?):\/\/(?:www\.)?youtu(?:be\.com\/watch\?v=|\.be\/)([\w\-\\_]*)(&(amp;)?‌​[\w\?‌​=]*)?");
                StringBuilder sb = new StringBuilder();

                // Writing to watch videos to file
                foreach (HtmlNode n in nodes)
                {
                    var video = regex.Matches(n.ParentNode.InnerHtml)[0].Value.Split('=')[1];
                    toWatch.Add(video);
                }
                foreach (String s in toWatch)
                {
                    sb.AppendLine(s);
                }
                File.WriteAllText(videoToWatchListPath, sb.ToString());

                sb.Clear();
                //Writing watched videos to file
                foreach (HtmlNode n in nodesWatched)
                {
                    sb.AppendLine(regex.Matches(n.ParentNode.InnerHtml)[0].Value.Split('=')[1]);
                }
                Thread.Sleep(1000);
                File.WriteAllText(watchedVideoPath, sb.ToString());
                sb.Clear();
                Console.WriteLine("Осталось роликов для просмотра: " + toWatch.Count);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        // Getting OAuth token to post and edit comments
        private static async Task GetOAuth()
        {
            using (var stream = new FileStream("client_secrets.json", FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    new[] { YouTubeService.Scope.YoutubeForceSsl,
                    YouTubeService.Scope.YoutubeReadonly

                    },
                    "user",
                    CancellationToken.None,
                    new FileDataStore(Environment.SpecialFolder.ApplicationData.ToString())
                );
            }
        }
        // Sending comment
        private static async Task<string> SendComment(string url)
        {
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = key,
                HttpClientInitializer = credential,
                ApplicationName = "YoutubeApp"
            });

            // Making a comment
            var commentThread = new CommentThread();
            CommentThreadSnippet snippet = new CommentThreadSnippet();
            Comment topLevelComment = new Comment();
            CommentSnippet commentSnippet = new CommentSnippet();
            commentSnippet.TextOriginal = GetRandomComment();
            topLevelComment.Snippet = commentSnippet;
            snippet.TopLevelComment = topLevelComment;
            snippet.VideoId = url;
            commentThread.Snippet = snippet;
            // Sending a query
            var query = youtubeService.CommentThreads.Insert(commentThread, "snippet");
            try
            {
                var resp = await query.ExecuteAsync();
                Console.WriteLine("Комментарий отправлен");
                return resp.Id;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return string.Empty;
        }

        // Getting duration of the current video
        // Returns 0 if there is no video
        private static async Task<double> GetVideoDuration(string url)
        {
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = key,
                HttpClientInitializer = credential,
                ApplicationName = "YoutubeApp"
            });

            // Creating request with contentDetails to get 

            var req = youtubeService.Videos.List("contentDetails");
            req.Id = url;
            try
            {
                var response = await req.ExecuteAsync();
                TimeSpan duration = XmlConvert.ToTimeSpan(response.Items[0].ContentDetails.Duration);
                return duration.TotalMilliseconds;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return 0;
        }

        // Updating comment
        private static async Task UpdateComment(object commentID)
        {
            string id = (string)commentID;
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = key,
                HttpClientInitializer = credential,
                ApplicationName = "YoutubeApp"
            });
            // Creating body of a comment with id of origin comment
            var commentThread = new CommentThread();
            CommentThreadSnippet snippet = new CommentThreadSnippet();
            Comment topLevelComment = new Comment();
            CommentSnippet commentSnippet = new CommentSnippet();
            commentSnippet.TextOriginal = GetRandomComment();
            topLevelComment.Snippet = commentSnippet;
            snippet.TopLevelComment = topLevelComment;
            snippet.VideoId = VideoUrl;
            commentThread.Id = id;
            commentThread.Snippet = snippet;
            // Creating request and sending
            var query = youtubeService.CommentThreads.Update(commentThread, "snippet");
            try
            {
                var resp = await query.ExecuteAsync();
                Console.WriteLine("Комментарий изменен");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        // Reading id from user
        private static void ReadId()
        {
            Console.WriteLine("Введите номер зачетной книжки:");
            current.Id = Console.ReadLine();
            videoListUrl = baseVideoListUrl + current.Id;
            WriteToConfig();
        }

        // Writing id to file
        private static void WriteToConfig()
        {
            string json = JsonConvert.SerializeObject(current, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        // Reading id from config
        private static void ReadFromConfig()
        {
            string json = File.ReadAllText(filePath);
            if (json == string.Empty)
            {
                ReadId();
                return;
            }
            current = JsonConvert.DeserializeObject<UserConfig>(json);
            videoListUrl = baseVideoListUrl + current.Id;
        }
    }
}

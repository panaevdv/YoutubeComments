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


// XPATH FOR ALL VIDEOS //a[contains(@href,'watch')]
// XPATH FOR NOT VIEWED VIDEOS //a[contains(@href,'watch')]/following::td[1][not(contains(text(),'1'))]

namespace DBBot
{
    class Program
    {

        public static string filePath = Directory.GetCurrentDirectory() + "\\config.txt";
        public static string videoToWatchListPath = Directory.GetCurrentDirectory() + "\\videoToWatch.txt";
        public static string watchedVideoPath = Directory.GetCurrentDirectory() + "\\watchedVideo.txt";
        private static UserCredential credential;
        private static string VideoUrl;
        private static string key = "AIzaSyCU5Ttu6PlH6Klap5Z6DmDExhxz9g9ZTeY";
        //private static string login = "17130445";
        private static string baseVideoListUrl = @"http://hsm.ugatu.su/yt/reports/?cube=1&userId=";
        private static string videoListUrl;
        private static List<string> toWatch = new List<string>();
        private static List<string> watchedVideos = new List<string>();
        private static UserConfig current = new UserConfig();
        private static bool isWatched = false;
        private static bool toClose = false;
        private static string[] stihi;
        static async Task Main(string[] args)
        {
            await Run();
        }


        private static async Task Run()
        {
            if (Check())
            {
                Console.WriteLine("Авторизируйтесь через свой Google-аккаунт для просмотра ролика.\n" +
                    "Страница должна автоматически открыться в браузере по умолчанию.");
                await GetOAuth();
                ReadPoems();
                Console.WriteLine("Получаем список непросмотренных видео...");
                if (!(File.Exists(videoToWatchListPath) & CheckVideoList()))
                    await GetVideoList();
                await Watch();
            }
        }

        private static async Task Watch()
        {
            double sec = 0;
            foreach (string s in toWatch)
            {
                if (toClose)
                {
                    WriteVideoList();
                    Environment.Exit(0);
                }
                VideoUrl = s;
                sec = await GetVideoDuration(s);
                string id = await SendComment(s);
                TimerCallback tm = new TimerCallback(UpdateComment);
                // создаем таймер
                Timer timer = new Timer(tm, id, 0, (int)sec);
                DateTime now = DateTime.Now;
                now = now.AddMilliseconds(sec + 1000);
                Thread timeThread = new Thread(new ParameterizedThreadStart(TimeLeft));
                Thread typeInThread = new Thread(ReadEnd);
                timeThread.Start(now);
                typeInThread.Start();
                timeThread.Join();
            }
        }

        private static void ReadPoems()
        {
            stihi = File.ReadAllLines(Directory.GetCurrentDirectory() + "\\stihi.txt");
        }

        private static string GetRandomPoem()
        {
            Random r = new Random();
            return stihi[r.Next(stihi.Length - 1)];
        }

        public static void ReadEnd()
        {
            ConsoleKeyInfo key = Console.ReadKey();
            while (key.Key != ConsoleKey.Escape)
            {
                key = Console.ReadKey();
            }
            toClose = true;
        }

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
            isWatched = true;
        }



        private static bool Check()
        {
            if (!File.Exists(filePath))
            {
                ReadId();
            }
            ReadFromConfig();
            string input = "3";
            while (input != "0")
            {
                Console.WriteLine("Номер зачетной книжки: " + current.Id);
                Console.WriteLine("Для запуска программы нажмите 1.\n" +
                    "Для смены номера зачетной книжки нажмите 2.\n" +
                    "Для выхода нажмите 0.");
                input = Console.ReadLine();
                switch (input)
                {
                    case "1":
                        {
                            return true;
                        }
                    case "2":
                        {
                            ReadId();
                            ReadFromConfig();
                            break;
                        }
                    case "0":
                        {
                            Environment.Exit(0);
                            break;
                        }
                }

            }
            return false;
        }

        // Проверяем, что есть непросмотренные видео
        private static bool CheckVideoList()
        {
            ReadVideoList();
            if (toWatch.Count > 0)
                return true;
            return false;
        }

        private static void WriteVideoList()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string s in toWatch)
            {
                sb.AppendLine(s);
            }
            File.WriteAllText(videoToWatchListPath, sb.ToString());
        }

        // Считываем список видео из файла
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

        private static void ReadWatchedVideoList()
        {
            watchedVideos = new List<string>();
            using (StreamReader rw = new StreamReader(watchedVideoPath))
            {
                while (!rw.EndOfStream)
                    watchedVideos.Add(rw.ReadLine());
            }
        }

        private static async Task GetVideoList()
        {
            HttpClient client = new HttpClient();
            try
            {
                VideoUrl = baseVideoListUrl + current.Id;
                HttpResponseMessage resp = await client.GetAsync(VideoUrl);
                var responseString = await resp.Content.ReadAsStringAsync();
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseString);
                var nodes = doc.DocumentNode.SelectNodes("//a[contains(@href,'watch')]/following::td[1][not(contains(text(),'1'))]");
                var nodesWatched = doc.DocumentNode.SelectNodes("//a[contains(@href,'watch')]/following::td[1][contains(text(),'1')]");
                if (nodes.Count == 0)
                {
                    Console.WriteLine("Все видео просмотрены.");
                    return;
                }
                Regex regex = new Regex(@"http(?:s?):\/\/(?:www\.)?youtu(?:be\.com\/watch\?v=|\.be\/)([\w\-\\_]*)(&(amp;)?‌​[\w\?‌​=]*)?");
                StringBuilder sb = new StringBuilder();

                // Записываем непросмотренные видео в файл
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
                // Записываем просмотренные видео в файл
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

        // Получение OAuth токена для отправки и редактирования документов
        // TODO : Разобраться с Scope и выбрать нужный
        private static async Task GetOAuth()
        {
            using (var stream = new FileStream("client_secrets.json", FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    new[] { YouTubeService.Scope.YoutubeForceSsl
                    },
                    "user",
                    CancellationToken.None,
                    new FileDataStore(Environment.SpecialFolder.ApplicationData.ToString())
                );
            }
        }
        // Отправка комментария
        private static async Task<string> SendComment(string url)
        {
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = key,
                HttpClientInitializer = credential,
                ApplicationName = "YoutubeApp"
            });
            // Создание тела комментария
            var commentThread = new CommentThread();
            CommentThreadSnippet snippet = new CommentThreadSnippet();
            Comment topLevelComment = new Comment();
            CommentSnippet commentSnippet = new CommentSnippet();
            commentSnippet.TextOriginal = GetRandomPoem();
            topLevelComment.Snippet = commentSnippet;
            snippet.TopLevelComment = topLevelComment;
            snippet.VideoId = url;
            commentThread.Snippet = snippet;
            // Создание реквеста с данным комментарием
            var query = youtubeService.CommentThreads.Insert(commentThread, "snippet");
            try
            {
                var resp = await query.ExecuteAsync();
                return resp.Id;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return string.Empty;
        }

        // Получение длительности видеоролика
        // Возвращаются секунды
        // 0 - если запрос не удался
        private static async Task<double> GetVideoDuration(string url)
        {
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = key,
                HttpClientInitializer = credential,
                ApplicationName = "YoutubeApp"
            });

            // Создаем реквест с получением contentDetails, отправляем запрос

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

        // Редактирование комментария
        private static async void UpdateComment(object commentID)
        {
            string id = (string)commentID;
            // Аналогично созданию, только commentThread.Id = комментарию
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = key,
                HttpClientInitializer = credential,
                ApplicationName = "YoutubeApp"
            });
            var commentThread = new CommentThread();
            CommentThreadSnippet snippet = new CommentThreadSnippet();
            Comment topLevelComment = new Comment();
            CommentSnippet commentSnippet = new CommentSnippet();
            commentSnippet.TextOriginal = GetRandomPoem();
            topLevelComment.Snippet = commentSnippet;
            snippet.TopLevelComment = topLevelComment;
            snippet.VideoId = VideoUrl;
            commentThread.Id = id;
            commentThread.Snippet = snippet;
            // Создание реквеста с данным комментарием
            var query = youtubeService.CommentThreads.Update(commentThread, "snippet");
            try
            {
                var resp = await query.ExecuteAsync();
                File.AppendAllText(watchedVideoPath, VideoUrl);
                toWatch.Remove(VideoUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }


        private static void ReadId()
        {
            Console.WriteLine("Введите номер зачетной книжки:");
            current.Id = Console.ReadLine();
            videoListUrl = baseVideoListUrl + current.Id;
            WriteToConfig();
        }

        private static void WriteToConfig()
        {
            string json = JsonConvert.SerializeObject(current, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

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

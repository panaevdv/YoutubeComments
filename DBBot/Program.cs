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
        private static UserConfig current;
        static async Task Main(string[] args)
        {
            /*await GetVideoList();
            Console.WriteLine("Done");*/
            await GetVideoList();
            Console.WriteLine("Done");
        }

        /*
        private static async Task Test()
        {
            await Task.Delay(10000);
            Console.WriteLine("10 sec");
        }*/

        private static async Task Run()
        {
            if (Check())
            {
                Console.WriteLine("Авторизируйтесь через свой Google-аккаунт для просмотра ролика.\n" +
                    "Страница должна автоматически открыться в браузере по умолчанию.");
                await GetOAuth();
                Console.WriteLine("Получаем список непросмотренных видео...");
                if (!(File.Exists(videoToWatchListPath) & CheckVideoList()))
                    await GetVideoList();
            }
        }

        private static async Task Watch()
        {
            double sec = 0;
            foreach (string s in toWatch)
            {
                sec = await GetVideoDuration(s);

                await SendComment(s);
            }
        }


        private static bool Check()
        {
            if (!File.Exists(filePath))
            {
                File.Create(filePath);
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

        // Считываем список видео из файла
        private static void ReadVideoList()
        {
            toWatch = new List<string>();
            using (StreamReader rw = new StreamReader(videoToWatchListPath))
            {
                while (!rw.EndOfStream)
                {
                    toWatch.Add(rw.ReadLine());
                }
            }
        }

        private static async Task GetVideoList()
        {
            HttpClient client = new HttpClient();
            try
            {
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
                    toWatch.Add(regex.Matches(n.ParentNode.InnerHtml)[0].Value.Split('=')[1]);
                }
                foreach (String s in toWatch)
                {
                    sb.AppendLine(s);
                }
                if (!File.Exists(videoToWatchListPath))
                    File.Create(videoToWatchListPath);
                File.WriteAllText(videoToWatchListPath, sb.ToString());

                sb.Clear();
                // Записываем просмотренные видео в файл
                foreach (HtmlNode n in nodesWatched)
                {
                    sb.AppendLine(regex.Matches(n.ParentNode.InnerHtml)[0].Value.Split('=')[1]);
                }
                if (!File.Exists(watchedVideoPath))
                    File.Create(watchedVideoPath);
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
            commentSnippet.TextOriginal = "Hello, Boys";
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
        private static async Task UpdateComment(string commentID)
        {
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
            commentSnippet.TextOriginal = "Old Comment";
            topLevelComment.Snippet = commentSnippet;
            snippet.TopLevelComment = topLevelComment;
            snippet.VideoId = VideoUrl;
            commentThread.Id = commentID;
            commentThread.Snippet = snippet;
            // Создание реквеста с данным комментарием
            var query = youtubeService.CommentThreads.Insert(commentThread, "snippet");
            try
            {
                var resp = await query.ExecuteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }


        private static void ReadId()
        {
            if (!File.Exists(filePath))
                File.Create(filePath);
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
            current = JsonConvert.DeserializeObject<UserConfig>(json);
            videoListUrl = baseVideoListUrl + current.Id;
        }
    }
}

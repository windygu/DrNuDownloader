﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using DrNuDownloader.Scrapers;
using DrNuDownloader.Scrapers.Json;
using Rtmp;

namespace DrNuDownloader.Clients
{
    public interface IEpisodeClient
    {
        event EventHandler<DurationEventArgs> Duration;
        event EventHandler<ElapsedEventArgs> Elapsed;
        void Download(Uri episodeUri);
    }

    public class EpisodeClient : IEpisodeClient
    {
        private readonly IResourceClient _resourceClient;
        private readonly IFileNameSanitizer _fileNameSanitizer;
        private readonly IDrNuRtmpStreamFactory _drNuRtmpStreamFactory;

        public event EventHandler<DurationEventArgs> Duration;
        public event EventHandler<ElapsedEventArgs> Elapsed;

        public EpisodeClient(IResourceClient resourceClient, IFileNameSanitizer fileNameSanitizer, IDrNuRtmpStreamFactory drNuRtmpStreamFactory)
        {
            if (resourceClient == null) throw new ArgumentNullException("resourceClient");
            if (fileNameSanitizer == null) throw new ArgumentNullException("fileNameSanitizer");
            if (drNuRtmpStreamFactory == null) throw new ArgumentNullException("drNuRtmpStreamFactory");

            _resourceClient = resourceClient;
            _fileNameSanitizer = fileNameSanitizer;
            _drNuRtmpStreamFactory = drNuRtmpStreamFactory;
        }

        public void Download(Uri episodeUri)
        {
            if (episodeUri == null) throw new ArgumentNullException("episodeUri");

            var request = WebRequest.CreateHttp(episodeUri);
            var response = request.GetResponse();

            var streamReader = new StreamReader(response.GetResponseStream());
            var html = streamReader.ReadToEnd();

            var regex = new Regex("resource:\\s*\"(?<uri>.*)\"");
            var match = regex.Match(html);
            if (!match.Success)
            {
                throw new ScraperException("Unable to find resource.");
            }

            var resourceUri = new Uri(match.Groups["uri"].Value);
            var resource = _resourceClient.GetResource(resourceUri);
            
            if (resource.Data == null)
            {
                throw new ScraperException("Data property not found.");
            }

            var data = resource.Data.FirstOrDefault();
            if (data == null)
            {
                throw new ScraperException("Data property contains no elemements.");
            }

            if (data.Assets == null)
            {
                throw new ScraperException("Assets property not found.");
            }

            var videoResourceAsset = data.Assets.FirstOrDefault() as VideoResourceAsset;
            if (videoResourceAsset == null)
            {
                throw new ScraperException("Video resource not found.");
            }

            var link = videoResourceAsset.Links.Where(l => l.Target == "Streaming")
                                                .OrderByDescending(l => l.Bitrate).First();
            var rtmpUri = new Uri(link.Uri);

            var fileName = _fileNameSanitizer.Sanitize(string.Format("{0}.flv", data.Title));

            using (var drNuRtmpStream = _drNuRtmpStreamFactory.CreateDrNuRtmpStream(rtmpUri))
            {
                drNuRtmpStream.Duration += (sender, args) => OnDuration(args);
                drNuRtmpStream.Elapsed += (sender, args) => OnElapsed(args);

                using (var fileStream = File.Create(fileName))
                {
                    drNuRtmpStream.CopyTo(fileStream);
                }
            }
        }

        protected virtual void OnDuration(DurationEventArgs durationEventArgs)
        {
            if (durationEventArgs == null) throw new ArgumentNullException("durationEventArgs");

            var handler = Duration;
            if (handler != null)
            {
                handler(this, durationEventArgs);
            }
        }

        protected virtual void OnElapsed(ElapsedEventArgs elapsedEventArgs)
        {
            if (elapsedEventArgs == null) throw new ArgumentNullException("elapsedEventArgs");

            var handler = Elapsed;
            if (handler != null)
            {
                handler(this, elapsedEventArgs);
            }
        }
    }
}
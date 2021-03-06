﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Benchmarks;
using Newtonsoft.Json.Linq;

namespace BombardierClient
{
    class Program
    {
        private static readonly HttpClient _httpClient;
        private static readonly HttpClientHandler _httpClientHandler;

        private static Dictionary<PlatformID, string> _bombardierUrls = new Dictionary<PlatformID, string>()
        {
            { PlatformID.Win32NT, "https://github.com/codesenberg/bombardier/releases/download/v1.2.4/bombardier-windows-amd64.exe" },
            { PlatformID.Unix, "https://github.com/codesenberg/bombardier/releases/download/v1.2.4/bombardier-linux-amd64" },
        };

        static Program()
        {
            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClientHandler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;

            _httpClient = new HttpClient(_httpClientHandler);
        }

        static async Task Main(string[] args)
        {
            Console.WriteLine("Bombardier Client");
            Console.WriteLine("args: " + String.Join(' ', args));

            var bombardierUrl = _bombardierUrls[Environment.OSVersion.Platform];
            var bombardierFileName = Path.GetFileName(bombardierUrl);

            using (var downloadStream = await _httpClient.GetStreamAsync(bombardierUrl))
            using (var fileStream = File.Create(bombardierFileName))
            {
                await downloadStream.CopyToAsync(fileStream);
                if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    Process.Start("chmod", "+x " + bombardierFileName);
                }
            }


            var process = new Process()
            {
                StartInfo = {
                    FileName = bombardierFileName,
                    Arguments = String.Join(' ', args) + " --print r --format json",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
                EnableRaisingEvents = true
            };

            var stringBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e != null && e.Data != null)
                {
                    stringBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();

            Console.WriteLine(stringBuilder);

            var document = JObject.Parse(stringBuilder.ToString());

            BenchmarksEventSource.Log.Metadata("bombardier/requests", "max", "sum", "Requests", "Total number of requests", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/badresponses", "max", "sum", "Bad responses", "Non-2xx or 3xx responses", "n0");

            BenchmarksEventSource.Log.Metadata("bombardier/latency/mean", "max", "sum", "Mean latency (us)", "Mean latency (us)", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/latency/max", "max", "sum", "Max latency (us)", "Max latency (us)", "n0");

            BenchmarksEventSource.Log.Metadata("bombardier/rps/max", "max", "sum", "Max RPS", "RPS: max", "n0");

            BenchmarksEventSource.Log.Metadata("bombardier/raw", "all", "all", "Raw results", "Raw results", "json");

            var total = 
                document["result"]["req1xx"].Value<int>()
                + document["result"]["req2xx"].Value<int>()
                + document["result"]["req3xx"].Value<int>()
                + document["result"]["req3xx"].Value<int>()
                + document["result"]["req4xx"].Value<int>()
                + document["result"]["req5xx"].Value<int>()
                + document["result"]["others"].Value<int>();

            var success = document["result"]["req2xx"].Value<int>() + document["result"]["req3xx"].Value<int>();

            BenchmarksEventSource.Measure("bombardier/requests", total);
            BenchmarksEventSource.Measure("bombardier/badresponses", total - success);

            BenchmarksEventSource.Measure("bombardier/latency/mean", document["result"]["latency"]["mean"].Value<double>());
            BenchmarksEventSource.Measure("bombardier/latency/max", document["result"]["latency"]["max"].Value<double>());

            BenchmarksEventSource.Measure("bombardier/rps/max", document["result"]["rps"]["max"].Value<double>());

            BenchmarksEventSource.Measure("bombardier/raw", stringBuilder.ToString());

        }
    }
}

// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="Nick Malaguti">
//   Copyright (C) 2013 Nick Malaguti
//   
//   Permission is hereby granted, free of charge, to any person obtaining a
//   copy of this software and associated documentation files (the "Software"),
//   to deal in the Software without restriction, including without limitation
//   the rights to use, copy, modify, merge, publish, distribute, sublicense,
//   and/or sell copies of the Software, and to permit persons to whom the
//   Software is furnished to do so, subject to the following conditions:
//   
//   The above copyright notice and this permission notice shall be included in
//   all copies or substantial portions of the Software.
//   
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
//   THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
//   FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
//   DEALINGS IN THE SOFTWARE.
// </copyright>
// <summary>
//   The program.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Maxel
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Security;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using CommandLine;

    /// <summary>
    /// The program.
    /// </summary>
    public class Program
    {
        #region Static Fields

        /// <summary>
        /// The console lock.
        /// </summary>
        private static object consoleLock;

        /// <summary>
        /// The counter.
        /// </summary>
        private static long counter;

        /// <summary>
        /// The counter lock.
        /// </summary>
        private static object counterLock;

        /// <summary>
        /// The cursor top.
        /// </summary>
        private static int cursorTop;

        /// <summary>
        /// The cursor width.
        /// </summary>
        private static int cursorWidth;

        /// <summary>
        /// The progress.
        /// </summary>
        private static int progress;

        /// <summary>
        /// The size.
        /// </summary>
        private static long size;

        /// <summary>
        /// The total.
        /// </summary>
        private static long total;

        #endregion

        #region Methods

        /// <summary>
        /// The get password.
        /// </summary>
        /// <returns>
        /// The <see cref="SecureString"/>.
        /// </returns>
        private static SecureString GetPassword()
        {
            var secureString = new SecureString();
            ConsoleKeyInfo key;

            Console.Write("Password: ");
            do
            {
                key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Backspace && secureString.Length > 0)
                {
                    secureString.RemoveAt(secureString.Length - 1);
                }
                else if (key.Key != ConsoleKey.Enter)
                {
                    secureString.AppendChar(key.KeyChar);
                }
            }
            while (key.Key != ConsoleKey.Enter);

            Console.WriteLine();

            return secureString;
        }

        /// <summary>
        /// The main.
        /// </summary>
        /// <param name="args">
        /// The args.
        /// </param>
        private static void Main(string[] args)
        {
            counterLock = new object();
            consoleLock = new object();
            var options = new Options();

            if (!Parser.Default.ParseArguments(args, options))
            {
                Usage(options);
                return;
            }

            if (string.IsNullOrEmpty(options.Uri))
            {
                Usage(options);
                return;
            }

            Uri uri;

            try
            {
                uri = new Uri(options.Uri);
            }
            catch (UriFormatException)
            {
                Usage(options);
                return;
            }

            NetworkCredential networkCredentials;

            if (!string.IsNullOrEmpty(options.Username) && string.IsNullOrEmpty(options.Password))
            {
                networkCredentials = new NetworkCredential(options.Username, GetPassword());
            }
            else
            {
                networkCredentials = new NetworkCredential(options.Username, options.Password);
            }

            lock (consoleLock)
            {
                cursorWidth = Console.WindowWidth - 3;
                for (cursorTop = Console.CursorTop; cursorTop + 1 > Console.BufferHeight - 3; cursorTop--)
                {
                    Console.WriteLine();
                }

                WriteSpeed("Starting...");
                WriteTimes(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0));
                Console.SetCursorPosition(0, cursorTop + 2);
                Console.Write('[');
                Console.SetCursorPosition(cursorWidth + 1, cursorTop + 2);
                Console.Write(']');
            }

            var reportCancellationTokenSource = new CancellationTokenSource();
            ReportSpeed(reportCancellationTokenSource.Token);

            var downloadAccelerator = new DownloadAccelerator(
                uri, 
                options.OutputFile, 
                options.NumberOfConnections, 
                networkCredentials, 
                options.IgnoreCertificateErrors, 
                options.ChunkSize);
            downloadAccelerator.DataApplied += ProgressCallback;
            size = downloadAccelerator.Size;

            downloadAccelerator.Download().Wait();
            reportCancellationTokenSource.Cancel();

            lock (consoleLock)
            {
                Console.SetCursorPosition(0, cursorTop + 3);
            }
        }

        /// <summary>
        /// The progress callback.
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="dataApplied">
        /// The data applied.
        /// </param>
        private static void ProgressCallback(object sender, int dataApplied)
        {
            lock (counterLock)
            {
                int oldProgress = progress;
                counter += dataApplied;
                total += dataApplied;
                progress = (int)Math.Floor(((double)total / size) * cursorWidth);

                if (progress != oldProgress)
                {
                    WriteProgress(progress);
                }
            }
        }

        /// <summary>
        /// The report speed.
        /// </summary>
        /// <param name="cancellationToken">
        /// The cancellation token.
        /// </param>
        private static async void ReportSpeed(CancellationToken cancellationToken)
        {
            var elapsedStopwatch = new Stopwatch();
            elapsedStopwatch.Start();

            var speedStopwatch = new Stopwatch();
            var speeds = new double[50];
            int loops = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                lock (counterLock)
                {
                    counter = 0;
                    speedStopwatch.Restart();
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(0.1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                double secondsLeft;
                lock (counterLock)
                {
                    speeds[loops++ % speeds.Length] = (double)counter / 1024 / speedStopwatch.Elapsed.TotalSeconds;
                    secondsLeft = total > 0
                                      ? (elapsedStopwatch.Elapsed.TotalSeconds / ((double)total / size))
                                        - elapsedStopwatch.Elapsed.TotalSeconds
                                      : 0;
                }
                
                WriteSpeed(speeds.Take(Math.Min(loops, speeds.Length)).Average());
                WriteTimes(elapsedStopwatch.Elapsed, TimeSpan.FromSeconds(secondsLeft));
            }

            elapsedStopwatch.Stop();

            WriteSpeed("Complete");
            WriteTimes(elapsedStopwatch.Elapsed, TimeSpan.FromSeconds(0));
        }

        /// <summary>
        /// The usage.
        /// </summary>
        /// <param name="options">
        /// The options.
        /// </param>
        private static void Usage(Options options)
        {
            Console.WriteLine(options.GetUsage());
        }

        /// <summary>
        /// The write line.
        /// </summary>
        /// <param name="left">
        /// The left.
        /// </param>
        /// <param name="top">
        /// The top.
        /// </param>
        /// <param name="message">
        /// The message.
        /// </param>
        /// <param name="args">
        /// The args.
        /// </param>
        private static void WriteLine(int left, int top, string message, params object[] args)
        {
            var stringBuilder = new StringBuilder(string.Format(CultureInfo.InvariantCulture, message, args));
            int width = Console.BufferWidth;
            while (stringBuilder.Length < width)
            {
                stringBuilder.Append(' ');
            }

            lock (consoleLock)
            {
                Console.SetCursorPosition(left, top);
                Console.Write(stringBuilder.ToString());
            }
        }

        /// <summary>
        /// The write progress.
        /// </summary>
        /// <param name="left">
        /// The left.
        /// </param>
        private static void WriteProgress(int left)
        {
            var stringBuilder = new StringBuilder();
            while (stringBuilder.Length < left)
            {
                stringBuilder.Append('*');
            }
            
            lock (consoleLock)
            {
                Console.SetCursorPosition(1, cursorTop + 2);
                Console.Write(stringBuilder.ToString());
            }
        }

        /// <summary>
        /// The write speed.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        private static void WriteSpeed(object message)
        {
            WriteLine(0, cursorTop, "KBps: {0:F2}", message);
        }

        /// <summary>
        /// The write times.
        /// </summary>
        /// <param name="elapsed">
        /// The elapsed.
        /// </param>
        /// <param name="left">
        /// The left.
        /// </param>
        private static void WriteTimes(TimeSpan elapsed, TimeSpan left)
        {
            WriteLine(0, cursorTop + 1, "Time Elapsed: {0:hh\\:mm\\:ss}, Time Left: {1:hh\\:mm\\:ss}", elapsed, left);
        }

        #endregion
    }
}
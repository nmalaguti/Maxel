// --------------------------------------------------------------------------------------------------------------------
// <copyright company="Nick Malaguti" file="DownloadAccelerator.cs">
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
//   The download accelerator.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Maxel
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.MemoryMappedFiles;
    using System.Linq;
    using System.Net;
    using System.Security.Authentication;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The download accelerator.
    /// </summary>
    internal class DownloadAccelerator
    {
        #region Fields

        /// <summary>
        /// The chunk size.
        /// </summary>
        private readonly int chunkSize;

        /// <summary>
        /// The network credential.
        /// </summary>
        private readonly NetworkCredential networkCredential;

        /// <summary>
        /// The number of chunks.
        /// </summary>
        private readonly long numberOfChunks;

        /// <summary>
        /// The number of connections lock.
        /// </summary>
        private readonly object numberOfConnectionsLock;

        /// <summary>
        /// The state lock.
        /// </summary>
        private readonly object stateLock;

        /// <summary>
        /// The chunk position.
        /// </summary>
        private long chunkPosition;

        /// <summary>
        /// The number of connections.
        /// </summary>
        private int numberOfConnections;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        /// Initializes a new instance of the <see cref="DownloadAccelerator"/> class.
        /// </summary>
        /// <param name="uri">
        /// The uri.
        /// </param>
        /// <param name="filename">
        /// The filename.
        /// </param>
        /// <param name="numberOfConnections">
        /// The number of connections.
        /// </param>
        /// <param name="networkCredential">
        /// The network credential.
        /// </param>
        /// <param name="ignoreCertificateErrors">
        /// If true, ignore certificate errors.
        /// </param>
        /// <param name="chunkSize">
        /// The chunk size.
        /// </param>
        public DownloadAccelerator(
            Uri uri, 
            string filename, 
            int numberOfConnections, 
            NetworkCredential networkCredential = null, 
            bool ignoreCertificateErrors = false, 
            int chunkSize = 1048576)
        {
            this.stateLock = new object();
            this.numberOfConnectionsLock = new object();

            this.Uri = uri;
            this.networkCredential = networkCredential;
            this.NumberOfConnections = numberOfConnections;
            this.chunkSize = chunkSize;
            this.chunkPosition = 0;

            ServicePointManager.DefaultConnectionLimit = this.NumberOfConnections;

            if (ignoreCertificateErrors)
            {
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            }

            this.Initialize();

            this.Filename = string.IsNullOrEmpty(filename) ? this.Filename : filename;
            this.numberOfChunks = (long)Math.Ceiling(this.Size / (double)this.chunkSize);
        }

        #endregion

        #region Public Events

        /// <summary>
        /// The data applied.
        /// </summary>
        public event EventHandler<int> DataApplied;

        #endregion

        #region Enums

        /// <summary>
        /// The status.
        /// </summary>
        private enum Status
        {
            /// <summary>
            /// Status incomplete.
            /// </summary>
            Incomplete, 

            /// <summary>
            /// Status complete.
            /// </summary>
            Complete, 
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the filename.
        /// </summary>
        public string Filename { get; private set; }

        /// <summary>
        /// Gets or sets the number of connections.
        /// </summary>
        public int NumberOfConnections
        {
            get
            {
                return this.numberOfConnections;
            }

            set
            {
                this.numberOfConnections = value < 1 ? 1 : value;
            }
        }

        /// <summary>
        /// Gets the size.
        /// </summary>
        public long Size { get; private set; }

        /// <summary>
        /// Gets the uri.
        /// </summary>
        public Uri Uri { get; private set; }

        #endregion

        #region Public Methods and Operators

        /// <summary>
        /// Downloads the uri and saves it to filename.
        /// </summary>
        /// <param name="cancellationToken">
        /// The cancellation token.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        public Task Download(CancellationToken cancellationToken = new CancellationToken())
        {
            return Task.Run(
                () =>
                    {
                        using (var fileStream = new FileStream(this.Filename, FileMode.Create))
                        {
                            fileStream.SetLength(this.Size);
                        }

                        using (var memoryMappedFile = MemoryMappedFile.CreateFromFile(this.Filename, FileMode.Open))
                        {
                            var tasks = new List<Tuple<Task, CancellationTokenSource>>();
                            for (var i = 0; i < this.numberOfConnections; i++)
                            {
                                var cancellationTokenSource = new CancellationTokenSource();
                                tasks.Add(
                                    Tuple.Create(
                                        this.StartDownloader(memoryMappedFile, cancellationTokenSource.Token), 
                                        cancellationTokenSource));
                            }

                            while (!cancellationToken.IsCancellationRequested)
                            {
                                tasks.Add(
                                    Tuple.Create<Task, CancellationTokenSource>(
                                        Task.Delay(TimeSpan.FromSeconds(0.5)), null));

                                int index = Task.WaitAny(tasks.Select(tuple => tuple.Item1).ToArray());
                                lock (this.stateLock)
                                {
                                    if (this.chunkPosition == this.numberOfChunks)
                                    {
                                        break;
                                    }
                                }

                                tasks.RemoveAt(index);
                                lock (this.numberOfConnectionsLock)
                                {
                                    while (tasks.Count != this.NumberOfConnections)
                                    {
                                        if (tasks.Count > this.NumberOfConnections)
                                        {
                                            Tuple<Task, CancellationTokenSource> tuple = tasks[tasks.Count - 1];
                                            tasks.RemoveAt(tasks.Count - 1);
                                            tuple.Item2.Cancel();
                                            tuple.Item1.Wait();
                                        }
                                        else
                                        {
                                            var cancellationTokenSource = new CancellationTokenSource();
                                            tasks.Add(
                                                Tuple.Create(
                                                    this.StartDownloader(
                                                        memoryMappedFile, cancellationTokenSource.Token), 
                                                    cancellationTokenSource));
                                        }
                                    }
                                }
                            }

                            if (cancellationToken.IsCancellationRequested)
                            {
                                foreach (var tuple in tasks.Where(tuple => tuple.Item2 != null))
                                {
                                    tuple.Item2.Cancel();
                                }
                            }

                            Task.WaitAll(tasks.Select(tuple => tuple.Item1).ToArray());
                        }
                    });
        }

        #endregion

        #region Methods

        /// <summary>
        /// The on data applied.
        /// </summary>
        /// <param name="amount">
        /// The amount.
        /// </param>
        protected virtual async void OnDataApplied(int amount)
        {
            if (this.DataApplied != null)
            {
                await Task.Run(() => this.DataApplied(this, amount));
            }
        }

        /// <summary>
        /// The copy next chunk async.
        /// </summary>
        /// <param name="memoryMappedFile">
        /// The memory mapped file.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        /// <exception cref="DownloadAcceleratorException">
        /// Thrown if the web server returns the wrong amount of data.
        /// </exception>
        private async Task<Status> CopyNextChunkAsync(MemoryMappedFile memoryMappedFile)
        {
            HttpWebRequest httpWebRequest = this.GetWebRequest();
            long chunkId;
            lock (this.stateLock)
            {
                if (this.chunkPosition == this.numberOfChunks)
                {
                    return Status.Complete;
                }

                chunkId = this.chunkPosition++;
            }

            long offset = chunkId * this.chunkSize;
            long length = Math.Min(this.chunkSize, this.Size - offset);
            httpWebRequest.AddRange(offset, offset + length - 1);

            using (WebResponse webResponse = await httpWebRequest.GetResponseAsync())
            {
                if (long.Parse(webResponse.Headers[HttpResponseHeader.ContentLength]) != length)
                {
                    throw new DownloadAcceleratorException("Server returned incorrect amount of data");
                }

                using (MemoryMappedViewStream memoryMappedViewStream = memoryMappedFile.CreateViewStream(offset, length))
                {
                    await this.CopyToWithProgressAsync(webResponse.GetResponseStream(), memoryMappedViewStream);
                }
            }

            return Status.Incomplete;
        }

        /// <summary>
        /// The copy to with progress async.
        /// </summary>
        /// <param name="source">
        /// The source.
        /// </param>
        /// <param name="destination">
        /// The destination.
        /// </param>
        /// <param name="bufferSize">
        /// The buffer size.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        private async Task CopyToWithProgressAsync(Stream source, Stream destination, int bufferSize = 4096)
        {
            var buffer = new byte[bufferSize];

            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, 0, bufferSize)) != 0)
            {
                await destination.WriteAsync(buffer, 0, bytesRead);
                this.OnDataApplied(bytesRead);
            }
        }

        /// <summary>
        /// The get web request.
        /// </summary>
        /// <param name="method">
        /// The method.
        /// </param>
        /// <returns>
        /// The <see cref="HttpWebRequest"/>.
        /// </returns>
        private HttpWebRequest GetWebRequest(string method = "GET")
        {
            HttpWebRequest httpWebRequest = WebRequest.CreateHttp(this.Uri);
            httpWebRequest.Credentials = this.networkCredential;
            httpWebRequest.Method = method;
            if (this.Uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && this.networkCredential != null)
            {
                httpWebRequest.PreAuthenticate = true;
            }

            return httpWebRequest;
        }

        /// <summary>
        /// The initialize.
        /// </summary>
        /// <exception cref="Exception">
        /// Thrown if downloading data will not be possible.
        /// </exception>
        private void Initialize()
        {
            HttpWebRequest httpWebRequest = this.GetWebRequest("HEAD");

            try
            {
                using (WebResponse webResponse = httpWebRequest.GetResponse())
                {
                    if (string.IsNullOrEmpty(webResponse.Headers[HttpResponseHeader.AcceptRanges]))
                    {
                        throw new DownloadAcceleratorException("Server doesn't support ranges. Aborting.");
                    }

                    this.Size = long.Parse(webResponse.Headers[HttpResponseHeader.ContentLength]);
                    this.Filename = Path.GetFileName(webResponse.ResponseUri.AbsolutePath);
                }
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                {
                    ex.Response.Close();
                }

                if (ex.InnerException != null && ex.InnerException.GetType() == typeof(AuthenticationException))
                {
                    throw new DownloadAcceleratorException("Unable to validate SSL certificate. Aborting.");
                }

                var httpWebResponse = ex.Response as HttpWebResponse;
                if (httpWebResponse != null)
                {
                    if (httpWebResponse.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new DownloadAcceleratorException("Authentication required. Aborting.");                        
                    }
                }

                throw;
            }
        }

        /// <summary>
        /// The start downloader.
        /// </summary>
        /// <param name="memoryMappedFile">
        /// The memory mapped file.
        /// </param>
        /// <param name="cancellationToken">
        /// The cancellation token.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        private async Task StartDownloader(MemoryMappedFile memoryMappedFile, CancellationToken cancellationToken)
        {
            while ((!cancellationToken.IsCancellationRequested)
                   && (await this.CopyNextChunkAsync(memoryMappedFile) == Status.Incomplete))
            {
            }
        }

        #endregion
    }
}
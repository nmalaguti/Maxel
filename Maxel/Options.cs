// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Options.cs" company="Nick Malaguti">
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
//   The options.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Maxel
{
    using System.Text;
    using CommandLine;
    using CommandLine.Text;

    /// <summary>
    /// The options.
    /// </summary>
    internal sealed class Options
    {
        #region Public Properties

        /// <summary>
        /// Gets or sets the chunk size.
        /// </summary>
        [Option('c', "chunksize", MetaValue = "INT", DefaultValue = 1048576, 
            HelpText = "Size of chunks to split the file into")]
        public int ChunkSize { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether ignore certificate errors.
        /// </summary>
        [Option('i', "ignorecerterrors", DefaultValue = false, HelpText = "Ignore ssl certificate errors")]
        public bool IgnoreCertificateErrors { get; set; }

        /// <summary>
        /// Gets or sets the number of connections.
        /// </summary>
        [Option('n', MetaValue = "INT", DefaultValue = 10, HelpText = "Number of concurrent connections")]
        public int NumberOfConnections { get; set; }

        /// <summary>
        /// Gets or sets the output file.
        /// </summary>
        [Option('o', "output", MetaValue = "FILE", HelpText = "Output file")]
        public string OutputFile { get; set; }

        /// <summary>
        /// Gets or sets the password.
        /// </summary>
        [Option('p', "password", MetaValue = "PASSWORD", HelpText = "Password")]
        public string Password { get; set; }

        /// <summary>
        /// Gets or sets the uri.
        /// </summary>
        [ValueOption(0)]
        public string Uri { get; set; }

        /// <summary>
        /// Gets or sets the username.
        /// </summary>
        [Option('u', "username", MetaValue = "USERNAME", HelpText = "Username")]
        public string Username { get; set; }

        #endregion

        #region Public Methods and Operators

        /// <summary>
        /// Gets the usage
        /// </summary>
        /// <returns>
        /// The <see cref="string"/>.
        /// </returns>
        [HelpOption]
        public string GetUsage()
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("Usage: Maxel.exe <uri>");
            stringBuilder.Append(HelpText.AutoBuild(this, current => HelpText.DefaultParsingErrorsHandler(this, current)));

            return stringBuilder.ToString();
        }

        #endregion
    }
}
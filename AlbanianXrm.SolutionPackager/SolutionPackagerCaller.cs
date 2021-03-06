﻿using AlbanianXrm.SolutionPackager.Properties;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using XrmToolBox.Extensibility;

namespace AlbanianXrm.SolutionPackager
{
    internal class SolutionPackagerCaller
    {
        private readonly AsyncWorkQueue asyncWorkQueue;
        private readonly RichTextBox txtOutput;

        private const string deleteFilesQuestion = "Delete files? [Yes/No/List]:";

        public SolutionPackagerCaller(AsyncWorkQueue asyncWorkQueue, RichTextBox txtOutput)
        {
            this.asyncWorkQueue = asyncWorkQueue ?? throw new ArgumentNullException(nameof(asyncWorkQueue));
            this.txtOutput = txtOutput ?? throw new ArgumentNullException(nameof(txtOutput));
        }

        public void ExtractSolution(Parameters @params)
        {

            asyncWorkQueue.Enqueue(new WorkAsyncInfo
            {
                Message = string.Format(CultureInfo.InvariantCulture, Resources.EXTRACTING_SOLUTION, new FileInfo(@params.ZipFile).Name),
                AsyncArgument = @params,
                Work = ExtractSolution,
                ProgressChanged = ExtractSolutionProgress,
                PostWorkCallBack = ExtractSolutionCompleted
            });
        }

        private void ExtractSolution(BackgroundWorker worker, DoWorkEventArgs args)
        {
            var @params = args.Argument as Parameters ?? throw new ArgumentNullException(nameof(args.Argument));

            string dir = Path.GetDirectoryName(typeof(SolutionPackagerCaller).Assembly.Location);
            string folder = Path.GetFileNameWithoutExtension(typeof(SolutionPackagerCaller).Assembly.Location);
            dir = Path.Combine(dir, folder);

            if (!File.Exists(Path.Combine(dir, "SolutionPackager.exe")))
            {
                args.Result = Resources.SOLUTIONPACKAGER_MISSING;
                return;
            }

            Process process = new Process()
            {
                StartInfo =
                {
                    FileName =  Path.Combine(dir,"SolutionPackager.exe"),
                    Arguments = $"/action:Extract /zipfile:\"{@params.ZipFile}\" /folder:\"{@params.OutputFolder}\"",
                    WorkingDirectory = dir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true
                },
                EnableRaisingEvents = true
            };

            if (!@params.AllowWrite)
            {
                process.StartInfo.Arguments += " /allowWrite:No";
            }
            if (@params.AllowDelete.HasValue)
            {
                process.StartInfo.Arguments += " /allowDelete:" + (@params.AllowDelete.Value ? "Yes" : "No");
            }

            //Report call parameters
            worker.ReportProgress(0, @params);
            process.Start();

            if (!process.StandardOutput.EndOfStream)
            {
                worker.ReportProgress(10);
                char[] buffer = new char[deleteFilesQuestion.Length];
                char[] ringBuffer = new char[deleteFilesQuestion.Length];
                int ringBufferPosition = 0;
                int lastNewLine = buffer.Length;
                while (!process.StandardOutput.EndOfStream)
                {
                    var chars = process.StandardOutput.ReadBlock(buffer, 0, lastNewLine);
                    lastNewLine = buffer.Length;
                    for (int i = 0; i < chars; i++)
                    {
                        if (buffer[i] == '\n')
                        {
                            lastNewLine = i + 1;
                        }
                        ringBuffer[ringBufferPosition] = buffer[i];
                        ringBufferPosition = (ringBufferPosition + 1) % ringBuffer.Length;
                    }
                    worker.ReportProgress(20, new string(buffer, 0, chars));
                    bool isDeleteFilesQuestion = true;
                    for (int i = deleteFilesQuestion.Length - 1; i >= 0; i--)
                    {
                        if (deleteFilesQuestion[i] != ringBuffer[(ringBufferPosition + i) % ringBuffer.Length])
                        {
                            isDeleteFilesQuestion = false;
                            break;
                        }
                    }
                    if (isDeleteFilesQuestion)
                    {
                        @params.StandardInput = process.StandardInput;
                        worker.ReportProgress(21, @params);
                    }
                }
                worker.ReportProgress(30);
            }

            if (!process.StandardError.EndOfStream)
            {
                worker.ReportProgress(40);
                while (!process.StandardError.EndOfStream)
                {
                    worker.ReportProgress(50, process.StandardError.ReadLine());
                }
                worker.ReportProgress(60);
            }

            process.WaitForExit();
            worker.ReportProgress(70, "Ended");

            if (@params.FormatXml)
            {
                var tempFile = Path.GetTempFileName();
                foreach (var xmlFile in Directory.GetFiles(@params.OutputFolder, "*.xml", SearchOption.AllDirectories))
                {
                    worker.ReportProgress(80, new FileInfo(xmlFile));
                    XmlDocument document = new XmlDocument();
                    document.Load(xmlFile);
                    XmlWriterSettings writerSettings = new XmlWriterSettings()
                    {
                        Indent = true,
                        NewLineHandling = NewLineHandling.Replace,
                        NewLineOnAttributes = true
                    };

                    using (StreamWriter sw = new StreamWriter(tempFile, append: false))
                    using (XmlWriter writer = XmlWriter.Create(sw, writerSettings))
                    {
                        document.WriteContentTo(writer);
                    }

                    File.Copy(tempFile, xmlFile, overwrite: true);
                    worker.ReportProgress(90, new FileInfo(xmlFile));
                }
            }
        }

        private void AppendArgument(string argument, string value)
        {
            txtOutput.SelectionStart = txtOutput.TextLength;
            txtOutput.SelectionFont = new Font(txtOutput.Font, FontStyle.Bold);
            txtOutput.AppendText(" /" + argument + ":");
            txtOutput.SelectionStart = txtOutput.TextLength;
            txtOutput.SelectionFont = txtOutput.Font;
            txtOutput.AppendText(value);
        }

        private void ExtractSolutionProgress(ProgressChangedEventArgs args)
        {
            switch (args.ProgressPercentage)
            {
                case 0:
                    {
                        var @params = args.UserState as Parameters ?? throw new ArgumentNullException(nameof(args.UserState));

                        txtOutput.Text = "";
                        txtOutput.SelectionStart = 0;
                        txtOutput.SelectionFont = new Font(txtOutput.Font.FontFamily, 12, FontStyle.Underline);
                        txtOutput.AppendText("Launch Command Line" + Environment.NewLine);
                        txtOutput.SelectionStart = txtOutput.TextLength;
                        txtOutput.SelectionFont = new Font(txtOutput.Font, FontStyle.Bold | FontStyle.Italic);
                        txtOutput.AppendText("SolutionPackager.exe");
                        AppendArgument("Action", "Extract");
                        AppendArgument("zipfile", $"\"{@params.ZipFile}\"");
                        AppendArgument("folder", $"\"{@params.OutputFolder}\"");
                        if (!@params.AllowWrite)
                        {
                            AppendArgument("allowWrite", "No");
                        }
                        if (@params.AllowDelete.HasValue)
                        {
                            AppendArgument("allowDelete", @params.AllowDelete.Value ? "Yes" : "No");
                        }
                        txtOutput.AppendText(Environment.NewLine + Environment.NewLine);
                        txtOutput.SelectionStart = txtOutput.TextLength;
                        txtOutput.SelectionFont = new Font(txtOutput.Font.FontFamily, 12, FontStyle.Underline);
                        txtOutput.AppendText("Program Output" + Environment.NewLine);
                        txtOutput.SelectionStart = txtOutput.TextLength;
                        txtOutput.SelectionFont = txtOutput.Font;
                    }
                    break;
                case 10:
                case 30:
                    txtOutput.AppendText(Environment.NewLine);
                    break;
                case 20:
                case 50:
                    txtOutput.AppendText(args.UserState as string);
                    break;
                case 40:
                case 60:
                    txtOutput.SelectionStart = txtOutput.TextLength;
                    txtOutput.SelectionColor = args.ProgressPercentage == 40 ? Color.Red : txtOutput.ForeColor;
                    txtOutput.AppendText(Environment.NewLine);
                    break;
                case 70:
                    txtOutput.AppendText(Environment.NewLine);
                    break;
                case 21:
                    {
                        var @params = args.UserState as Parameters;
                        var dialogResponse = MessageBox.Show("There are unnecessary files. Do you want to delete them?", Resources.MBOX_INFORMATION, MessageBoxButtons.YesNo);
                        var response = dialogResponse == DialogResult.Yes ? (@params.AllowWrite ? "Yes" : "List") : "No";
                        @params.StandardInput.WriteLine(response);
                        txtOutput.AppendText(response + Environment.NewLine);
                    }
                    break;
            }
        }

        private void ExtractSolutionCompleted(RunWorkerCompletedEventArgs args)
        {
            if (args.Error != null)
            {
                MessageBox.Show(args.Error.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (args.Result != null)
            {
                MessageBox.Show(args.Result.ToString(), "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        public class Parameters
        {
            public string ZipFile { get; set; }

            public string OutputFolder { get; set; }

            public bool FormatXml { get; set; }

            public bool AllowWrite { get; set; }

            public bool? AllowDelete { get; set; }

            public StreamWriter StandardInput { get; set; }
        }
    }
}

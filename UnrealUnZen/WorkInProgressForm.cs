using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using UEcastocLib;
using static UEcastocLib.Packer;
using static UEcastocLib.UCasDataParser;

namespace UnrealUnZen
{
    public partial class WorkInProgressForm : Form
    {
        private readonly List<IAsyncResult> UIBeginInvokeCalls = new List<IAsyncResult>();

        public WorkInProgressForm()
        {
            InitializeComponent();
        }

        private void ProcessUIBeginInvokeCalls()
        {
            UIBeginInvokeCalls.RemoveAll(AsyncResult => AsyncResult.IsCompleted);
        }

        private bool IsEligibleToUpdateUIAgain()
        {
            return UIBeginInvokeCalls.Count == 0;
        }

        private void InvokeNonBlocking(Delegate method)
        {
            ProcessUIBeginInvokeCalls();

            if (!IsEligibleToUpdateUIAgain()) return;

            var asyncResult = BeginInvoke(method);

            UIBeginInvokeCalls.Add(asyncResult);
        }

        private const ulong Gigabyte = 1073741824;
        public void OnFileUnpacked(FileUnpackedEventArguments fileUnpackedEventArguments)
        {
            InvokeNonBlocking((MethodInvoker)delegate
            {
                ulong Gigabyte = (ulong)Math.Pow(2.0, 30.0);
                var currentFileNumber = fileUnpackedEventArguments.CurrentFileNumber;
                var totalFilesNumber = fileUnpackedEventArguments.TotalFilesNumber;
                var filesUnpackedSize = fileUnpackedEventArguments.FilesUnpackedSize;
                var allFilesSize = fileUnpackedEventArguments.AllFilesSize;
                double filesUnpackedSizeGB = (double)filesUnpackedSize / Gigabyte;
                double allFilesSizeGB = (double)allFilesSize / Gigabyte;

                ProgressLabel.Text = 
                    $"Unpacked {currentFileNumber} out of {totalFilesNumber} files\n" +
                    $"{filesUnpackedSizeGB:0.##} / {allFilesSizeGB:0.##} GB";
            });
        }

        public void OnFinishedUnpacking(int filesUnpacked)
        {
            MessageBox.Show(filesUnpacked + " file(s) extracted!");
            Invoke((MethodInvoker)Close);
        }

        public void OnManifestFileProcessed(FileProcessedEventArguments fileProcessedEventArguments)
        {
            InvokeNonBlocking((MethodInvoker)delegate
            {
                var currentFileNumber = fileProcessedEventArguments.CurrentFileNumber;
                var totalFilesNumber = fileProcessedEventArguments.TotalFilesNumber;
                var filesUnpackedSize = fileProcessedEventArguments.FilesUnpackedSize;
                var allFilesSize = fileProcessedEventArguments.AllFilesSize;
                double filesUnpackedSizeGB = (double)filesUnpackedSize / Gigabyte;
                double allFilesSizeGB = (double)allFilesSize / Gigabyte;

                ProgressLabel.Text =
                    $"Processed {currentFileNumber} out of {totalFilesNumber} manifest files";
            });
        }

        public void OnFilePacked(FileProcessedEventArguments fileProcessedEventArguments)
        {
            InvokeNonBlocking((MethodInvoker)delegate
            {
                var currentFileNumber = fileProcessedEventArguments.CurrentFileNumber;
                var totalFilesNumber = fileProcessedEventArguments.TotalFilesNumber;
                var filesUnpackedSize = fileProcessedEventArguments.FilesUnpackedSize;
                var allFilesSize = fileProcessedEventArguments.AllFilesSize;
                double filesUnpackedSizeGB = (double)filesUnpackedSize / Gigabyte;
                double allFilesSizeGB = (double)allFilesSize / Gigabyte;

                ProgressLabel.Text =
                    $"Packed {currentFileNumber} out of {totalFilesNumber} files\n" +
                    $"{filesUnpackedSizeGB:0.##} / {allFilesSizeGB:0.##} GB";
            });
        }

        public void OnFinishedPacking(int filesPacked)
        {
            MessageBox.Show(filesPacked + " file(s) extracted!");
            Invoke((MethodInvoker)Close);
        }

        public void OnPakWritten(long bytesWrittenTotal, long pakBytesTotal)
        {
            InvokeNonBlocking((MethodInvoker)delegate
            {
                double bytesWrittenTotalGB = (double)bytesWrittenTotal / Gigabyte;
                double pakBytesTotalGB = (double)pakBytesTotal / Gigabyte;

                ProgressLabel.Text =
                    $"Saving .pak file\n" +
                    $"{bytesWrittenTotalGB:0.##} / {pakBytesTotalGB:0.##} GB";
            });
        }

        public void OnFinishedParsingUtoc()
        {
            Invoke((MethodInvoker)Close);
        }

        public void OnParsingUtocStageEvent(string stageName)
        {
            Invoke((MethodInvoker) delegate
            {
                ProgressLabel.Text = stageName;
            });
        }

        public void OnMakeTreeFileProcessed(int currentFileNumber, int totalNumberOfFiles)
        {
            InvokeNonBlocking((MethodInvoker)delegate
            {
                ProgressLabel.Text =
                    $"Creating file tree\n" +
                    $"Processed {currentFileNumber} out of {totalNumberOfFiles} manifest files";
            });
        }
    }
}

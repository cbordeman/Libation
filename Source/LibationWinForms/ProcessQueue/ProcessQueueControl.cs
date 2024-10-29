﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApplicationServices;
using LibationFileManager;
using LibationUiBase;
using LibationUiBase.ViewModels.Player;

namespace LibationWinForms.ProcessQueue
{
    internal partial class SidebarControl : UserControl, ILogForm
    {
        private TrackedQueue<ProcessBook> Queue = new();
        private readonly LogMe Logger;
        private int QueuedCount
        {
            set
            {
                queueNumberLbl.Text = value.ToString();
                queueNumberLbl.Visible = value > 0;
            }
        }
        private int ErrorCount
        {
            set
            {
                errorNumberLbl.Text = value.ToString();
                errorNumberLbl.Visible = value > 0;
            }
        }

        private int CompletedCount
        {
            set
            {
                completedNumberLbl.Text = value.ToString();
                completedNumberLbl.Visible = value > 0;
            }
        }

        public Task QueueRunner { get; private set; }
        public bool Running => !QueueRunner?.IsCompleted ?? false;
        public ToolStripButton popoutBtn = new();

        private PlayerViewModel player = ServiceLocator.Get<PlayerViewModel>();
        
        public SidebarControl()
        {
            InitializeComponent();

            var speedLimitMBps = Configuration.Instance.DownloadSpeedLimit / 1024m / 1024;
            numericUpDown1.Value = speedLimitMBps > numericUpDown1.Maximum || speedLimitMBps < numericUpDown1.Minimum ? 0 : speedLimitMBps;

            popoutBtn.DisplayStyle = ToolStripItemDisplayStyle.Text;
            popoutBtn.Name = "popoutBtn";
            popoutBtn.Text = "Pop Out";
            popoutBtn.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            popoutBtn.Alignment = ToolStripItemAlignment.Right;
            popoutBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            statusStrip1.Items.Add(popoutBtn);

            Logger = LogMe.RegisterForm(this);

            virtualFlowControl2.RequestData += VirtualFlowControl1_RequestData;
            virtualFlowControl2.ButtonClicked += VirtualFlowControl2_ButtonClicked;

            Queue.QueuededCountChanged += Queue_QueuededCountChanged;
            Queue.CompletedCountChanged += Queue_CompletedCountChanged;

            Load += SidebarControl_Load;
        }

        BindingSource playlistBindingSource;

        private void SidebarControl_Load(object sender, EventArgs e)
        {
            if (DesignMode) return;

            runningTimeLbl.Text = string.Empty;
            QueuedCount = 0;
            ErrorCount = 0;
            CompletedCount = 0;

            // Setup binding to the playlist and its selected item
            playlistPage.DataContext = player;
            playlistDataGridView.DataSource = new BindingSource(
                player, nameof(PlayerViewModel.PlaylistItems));
            playlistDataGridView.AddSelectedItemBinding(player, x => x.SelectedBook);

            upButton.Command = player.MoveUpCommand;
            downButton.Command = player.MoveDownCommand;
        }


        private bool isBookInQueue(DataLayer.LibraryBook libraryBook)
            => Queue.Any(b => b?.LibraryBook?.Book?.AudibleProductId == libraryBook.Book.AudibleProductId);

        public void AddDownloadPdf(DataLayer.LibraryBook libraryBook)
            => AddDownloadPdf(new List<DataLayer.LibraryBook>() { libraryBook });

        public void AddDownloadDecrypt(DataLayer.LibraryBook libraryBook)
            => AddDownloadDecrypt(new List<DataLayer.LibraryBook>() { libraryBook });

        public void AddConvertMp3(DataLayer.LibraryBook libraryBook)
            => AddConvertMp3(new List<DataLayer.LibraryBook>() { libraryBook });

        public void AddDownloadPdf(IEnumerable<DataLayer.LibraryBook> entries)
        {
            List<ProcessBook> procs = new();
            foreach (var entry in entries)
            {
                if (isBookInQueue(entry))
                    continue;

                ProcessBook pbook = new(entry, Logger);
                pbook.PropertyChanged += Pbook_DataAvailable;
                pbook.AddDownloadPdf();
                procs.Add(pbook);
            }

            Serilog.Log.Logger.Information("Queueing {count} books", procs.Count);
            AddToQueue(procs);
        }

        public void AddDownloadDecrypt(IEnumerable<DataLayer.LibraryBook> entries)
        {
            List<ProcessBook> procs = new();
            foreach (var entry in entries)
            {
                if (isBookInQueue(entry))
                    continue;

                ProcessBook pbook = new(entry, Logger);
                pbook.PropertyChanged += Pbook_DataAvailable;
                pbook.AddDownloadDecryptBook();
                pbook.AddDownloadPdf();
                procs.Add(pbook);
            }

            Serilog.Log.Logger.Information("Queueing {count} books", procs.Count);
            AddToQueue(procs);
        }
        
        public void AddConvertMp3(IEnumerable<DataLayer.LibraryBook> entries)
        {
            List<ProcessBook> procs = new();
            foreach (var entry in entries)
            {
                if (isBookInQueue(entry))
                    continue;

                ProcessBook pbook = new(entry, Logger);
                pbook.PropertyChanged += Pbook_DataAvailable;
                pbook.AddConvertToMp3();
                procs.Add(pbook);
            }

            Serilog.Log.Logger.Information("Queueing {count} books", procs.Count);
            AddToQueue(procs);
        }
        private void AddToQueue(IEnumerable<ProcessBook> pbook)
        {
            BeginInvoke(() =>
            {
                Queue.Enqueue(pbook);
                if (!Running)
                    QueueRunner = QueueLoop();
            });
        }

        DateTime StartingTime;
        private async Task QueueLoop()
        {
            try
            {
                Serilog.Log.Logger.Information("Begin processing queue");

                StartingTime = DateTime.Now;
                counterTimer.Start();

                bool shownServiceOutageMessage = false;

                while (Queue.MoveNext())
                {
                    var nextBook = Queue.Current;

                    Serilog.Log.Logger.Information("Begin processing queued item. {item_LibraryBook}", nextBook?.LibraryBook);

                    var result = await nextBook.ProcessOneAsync();

                    Serilog.Log.Logger.Information("Completed processing queued item: {item_LibraryBook}\r\nResult: {result}", nextBook?.LibraryBook, result);

                    if (result == ProcessBookResult.ValidationFail)
                        Queue.ClearCurrent();
                    else if (result == ProcessBookResult.FailedAbort)
                        Queue.ClearQueue();
                    else if (result == ProcessBookResult.FailedSkip)
                        nextBook.LibraryBook.UpdateBookStatus(DataLayer.LiberatedStatus.Error);
                    else if (result == ProcessBookResult.LicenseDeniedPossibleOutage && !shownServiceOutageMessage)
                    {
                        MessageBox.Show(@$"
You were denied a content license for {nextBook.LibraryBook.Book.TitleWithSubtitle}

This error appears to be caused by a temporary interruption of service that sometimes affects Libation's users. This type of error usually resolves itself in 1 to 2 days, and in the meantime you should still be able to access your books through Audible's website or app.
",
                        "Possible Interruption of Service",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Asterisk);
                        shownServiceOutageMessage = true;
                    }
                }
                Serilog.Log.Logger.Information("Completed processing queue");

                Queue_CompletedCountChanged(this, 0);
                counterTimer.Stop();
                virtualFlowControl2.VirtualControlCount = Queue.Count;
                UpdateAllControls();
            }
            catch (Exception ex)
            {
                Serilog.Log.Logger.Error(ex, "An error was encountered while processing queued items");
            }
        }

        public void WriteLine(string text)
        {
            if (IsDisposed) return;

            var timeStamp = DateTime.Now;
            Invoke(() => logDGV.Rows.Add(timeStamp, text.Trim()));
        }

        #region Control event handlers

        private void Queue_CompletedCountChanged(object sender, int e)
        {
            int errCount = Queue.Completed.Count(p => p.Result is ProcessBookResult.FailedAbort or ProcessBookResult.FailedSkip or ProcessBookResult.FailedRetry or ProcessBookResult.ValidationFail);
            int completeCount = Queue.Completed.Count(p => p.Result is ProcessBookResult.Success);

            ErrorCount = errCount;
            CompletedCount = completeCount;
            UpdateProgressBar();
        }
        private void Queue_QueuededCountChanged(object sender, int cueCount)
        {
            QueuedCount = cueCount;
            virtualFlowControl2.VirtualControlCount = Queue.Count;
            UpdateProgressBar();
        }
        private void UpdateProgressBar()
        {
            toolStripProgressBar1.Maximum = Queue.Count;
            toolStripProgressBar1.Value = Queue.Completed.Count;
        }

        private async void cancelAllBtn_Click(object sender, EventArgs e)
        {
            Queue.ClearQueue();
            if (Queue.Current is not null)
                await Queue.Current.CancelAsync();
            virtualFlowControl2.VirtualControlCount = Queue.Count;
            UpdateAllControls();
        }

        private void btnClearFinished_Click(object sender, EventArgs e)
        {
            Queue.ClearCompleted();
            virtualFlowControl2.VirtualControlCount = Queue.Count;
            UpdateAllControls();

            if (!Running)
                runningTimeLbl.Text = string.Empty;
        }

        private void CounterTimer_Tick(object sender, EventArgs e)
        {
            string timeToStr(TimeSpan time)
            {
                string minsSecs = $"{time:mm\\:ss}";
                if (time.TotalHours >= 1)
                    return $"{time.TotalHours:F0}:{minsSecs}";
                return minsSecs;
            }

            if (Running)
                runningTimeLbl.Text = timeToStr(DateTime.Now - StartingTime);
        }

        private void clearLogBtn_Click(object sender, EventArgs e)
        {
            logDGV.Rows.Clear();
        }

        private void LogCopyBtn_Click(object sender, EventArgs e)
        {
            string logText = string.Join("\r\n", logDGV.Rows.Cast<DataGridViewRow>().Select(r => $"{r.Cells[0].Value}\t{r.Cells[1].Value}"));
            Clipboard.SetDataObject(logText, false, 5, 150);
        }

        private void LogDGV_Resize(object sender, EventArgs e)
        {
            logDGV.Columns[1].Width = logDGV.Width - logDGV.Columns[0].Width;
        }

        #endregion

        #region View-Model update event handling

        /// <summary>
        /// Index of the first <see cref="ProcessBook"/> visible in the <see cref="VirtualFlowControl"/>
        /// </summary>
        private int FirstVisible = 0;
        /// <summary>
        /// Number of <see cref="ProcessBook"/> visible in the <see cref="VirtualFlowControl"/>
        /// </summary>
        private int NumVisible = 0;
        /// <summary>
        /// Controls displaying the <see cref="ProcessBook"/> state, starting with <see cref="FirstVisible"/> 
        /// </summary>
        private IReadOnlyList<ProcessBookControl> Panels;

        /// <summary>
        /// Updates the display of a single <see cref="ProcessBookControl"/> at <paramref name="queueIndex"/> within <see cref="Queue"/>
        /// </summary>
        /// <param name="queueIndex">index of the <see cref="ProcessBook"/> within the <see cref="Queue"/></param>
        /// <param name="propertyName">The nme of the property that needs updating. If null, all properties are updated.</param>
        private void UpdateControl(int queueIndex, string propertyName = null)
        {
            try
            {
                int i = queueIndex - FirstVisible;

                if (i > NumVisible || i < 0) return;

                var proc = Queue[queueIndex];

                Invoke(() =>
                {
                    Panels[i].SuspendLayout();
                    if (propertyName is null or nameof(proc.Cover))
                        Panels[i].SetCover(proc.Cover);
                    if (propertyName is null or nameof(proc.BookText))
                        Panels[i].SetBookInfo(proc.BookText);

                    if (proc.Result != ProcessBookResult.None)
                    {
                        Panels[i].SetResult(proc.Result);
                        return;
                    }

                    if (propertyName is null or nameof(proc.Status))
                        Panels[i].SetStatus(proc.Status);
                    if (propertyName is null or nameof(proc.Progress))
                        Panels[i].SetProgrss(proc.Progress);
                    if (propertyName is null or nameof(proc.TimeRemaining))
                        Panels[i].SetRemainingTime(proc.TimeRemaining);
                    Panels[i].ResumeLayout();
                });
            }
            catch (Exception ex)
            {
                Serilog.Log.Logger.Error(ex, "Error updating the queued item's display.");
            }
        }

        private void UpdateAllControls()
        {
            int numToShow = Math.Min(NumVisible, Queue.Count - FirstVisible);

            for (int i = 0; i < numToShow; i++)
                UpdateControl(FirstVisible + i);
        }


        /// <summary>
        /// View notified the model that a botton was clicked
        /// </summary>
        /// <param name="queueIndex">index of the <see cref="ProcessBook"/> within <see cref="Queue"/></param>
        /// <param name="panelClicked">The clicked control to update</param>
        private async void VirtualFlowControl2_ButtonClicked(int queueIndex, string buttonName, ProcessBookControl panelClicked)
        {
            try
            {
                ProcessBook item = Queue[queueIndex];
                if (buttonName == nameof(panelClicked.cancelBtn))
                {
                    if (item is not null)
                        await item.CancelAsync();
                    Queue.RemoveQueued(item);
                    virtualFlowControl2.VirtualControlCount = Queue.Count;
                }
                else if (buttonName == nameof(panelClicked.moveFirstBtn))
                {
                    Queue.MoveQueuePosition(item, QueuePosition.Fisrt);
                    UpdateAllControls();
                }
                else if (buttonName == nameof(panelClicked.moveUpBtn))
                {
                    Queue.MoveQueuePosition(item, QueuePosition.OneUp);
                    UpdateControl(queueIndex);
                    if (queueIndex > 0)
                        UpdateControl(queueIndex - 1);
                }
                else if (buttonName == nameof(panelClicked.moveDownBtn))
                {
                    Queue.MoveQueuePosition(item, QueuePosition.OneDown);
                    UpdateControl(queueIndex);
                    if (queueIndex + 1 < Queue.Count)
                        UpdateControl(queueIndex + 1);
                }
                else if (buttonName == nameof(panelClicked.moveLastBtn))
                {
                    Queue.MoveQueuePosition(item, QueuePosition.Last);
                    UpdateAllControls();
                }
            }
            catch(Exception ex)
            {
                Serilog.Log.Logger.Error(ex, "Error handling button click from queued item");
            }
        }

        /// <summary>
        /// View needs updating
        /// </summary>
        private void VirtualFlowControl1_RequestData(int firstIndex, int numVisible, IReadOnlyList<ProcessBookControl> panelsToFill)
        {
            FirstVisible = firstIndex;
            NumVisible = numVisible;
            Panels = panelsToFill;
            UpdateAllControls();
        }

        /// <summary>
        /// Model updates the view
        /// </summary>
        private void Pbook_DataAvailable(object sender, PropertyChangedEventArgs e)
        {
            int index = Queue.IndexOf((ProcessBook)sender);
            UpdateControl(index, e.PropertyName);
        }

        #endregion

        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            var newValue = (long)(numericUpDown1.Value * 1024 * 1024);

            var config = Configuration.Instance;
            config.DownloadSpeedLimit = newValue;
            if (config.DownloadSpeedLimit > newValue)
                numericUpDown1.Value =
                    numericUpDown1.Value == 0.01m ? config.DownloadSpeedLimit / 1024m / 1024
                    : 0;

            numericUpDown1.Increment =
                numericUpDown1.Value > 100 ? 10
                : numericUpDown1.Value > 10 ? 1
                : numericUpDown1.Value > 1 ? 0.1m
                : 0.01m;

            numericUpDown1.DecimalPlaces =
                numericUpDown1.Value >= 10 ? 0
                : numericUpDown1.Value >= 1 ? 1
                : 2;
        }
    }
    public class NumericUpDownSuffix : NumericUpDown
    {
        [Description("Suffix displayed after numeric value."), Category("Data")]
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [DisallowNull]
        public string Suffix
        {
            get => _suffix;
            set
            {
                base.Text = string.IsNullOrEmpty(_suffix) ? base.Text : base.Text.Replace(_suffix, value);
                _suffix = value;
                ChangingText = true;
            }
        }
        private string _suffix = string.Empty;
        public override string Text
        {
            get => string.IsNullOrEmpty(Suffix) ? base.Text : base.Text.Replace(Suffix, string.Empty);
            set
            {
                if (Value == Minimum)
                    base.Text = "∞";
                else
                    base.Text = value + Suffix;
            }
        }
    }
}

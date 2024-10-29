using ApplicationServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using DataLayer;
using Dinah.Core;
using FileLiberator;
using LibationAvalonia.Controls;
using LibationAvalonia.Dialogs;
using LibationAvalonia.ViewModels;
using LibationFileManager;
using LibationUiBase.GridView;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppScaffolding;
using LibationUiBase;
using System.Diagnostics;
using Dinah.Core.Logging;
using LibationUiBase.ViewModels.Player;

namespace LibationAvalonia.Views
{
    public partial class ProductsDisplay : UserControl
    {
        public event EventHandler<LibraryBook> LiberateClicked;
        public event EventHandler<ISeriesEntry> LiberateSeriesClicked;
        public event EventHandler<LibraryBook> ConvertToMp3Clicked;

        private PlayerViewModel _playerViewModel = ServiceLocator.Get<PlayerViewModel>();
        private ProductsDisplayViewModel _viewModel => DataContext as ProductsDisplayViewModel;
        ImageDisplayDialog imageDisplayDialog;

        public ProductsDisplay()
        {
            InitializeComponent();
            DataGridContextMenus.CellContextMenuStripNeeded += ProductsGrid_CellContextMenuStripNeeded;

            var cellSelector = Selectors.Is<DataGridCell>(null);
            rowHeightStyle = new Style(_ => cellSelector);
            rowHeightStyle.Setters.Add(rowHeightSetter);

            var tboxSelector = cellSelector.Descendant().Is<TextBlock>();
            fontSizeStyle = new Style(_ => tboxSelector);
            fontSizeStyle.Setters.Add(fontSizeSetter);

            var tboxH1Selector = cellSelector.Child().Is<Panel>().Child().Is<TextBlock>().Class("h1");
            fontSizeH1Style = new Style(_ => tboxH1Selector);
            fontSizeH1Style.Setters.Add(fontSizeH1Setter);

            var tboxH2Selector = cellSelector.Child().Is<Panel>().Child().Is<TextBlock>().Class("h2");
            fontSizeH2Style = new Style(_ => tboxH2Selector);
            fontSizeH2Style.Setters.Add(fontSizeH2Setter);

            Configuration.Instance.PropertyChanged += Configuration_GridScaleChanged;
            Configuration.Instance.PropertyChanged += Configuration_FontChanged;

            if (Design.IsDesignMode)
            {
                using var context = DbContexts.GetContext();
                List<LibraryBook> sampleEntries;
                try
                {
                    sampleEntries = new()
                    {
						//context.GetLibraryBook_Flat_NoTracking("B00DCD0OXU"),try{
						context.GetLibraryBook_Flat_NoTracking("B017WJ5ZK6"),
                        context.GetLibraryBook_Flat_NoTracking("B017V4IWVG"),
                        context.GetLibraryBook_Flat_NoTracking("B017V4JA2Q"),
                        context.GetLibraryBook_Flat_NoTracking("B017V4NUPO"),
                        context.GetLibraryBook_Flat_NoTracking("B017V4NMX4"),
                        context.GetLibraryBook_Flat_NoTracking("B017V4NOZ0"),
                        context.GetLibraryBook_Flat_NoTracking("B017WJ5ZK6")
                    };
                }
                catch { sampleEntries = new(); }

                var pdvm = new ProductsDisplayViewModel();
                _ = pdvm.BindToGridAsync(sampleEntries);
                DataContext = pdvm;

                setGridScale(1);
                setFontScale(1);
                return;
            }

            setGridScale(Configuration.Instance.GridScaleFactor);
            setFontScale(Configuration.Instance.GridFontScaleFactor);
            Configure_ColumnCustomization();

            foreach (var column in productsGrid.Columns)
            {
                column.CustomSortComparer = new RowComparer(column);
            }
        }

        private void RemoveColumn_PropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (sender is DataGridColumn col && e.Property == DataGridColumn.IsVisibleProperty)
            {
                col.DisplayIndex = 0;
                col.CanUserReorder = false;
            }
        }

        #region Scaling

        [PropertyChangeFilter(nameof(Configuration.GridScaleFactor))]
        private void Configuration_GridScaleChanged(object sender, Dinah.Core.PropertyChangedEventArgsEx e)
        {
            setGridScale((float)e.NewValue);
        }

        [PropertyChangeFilter(nameof(Configuration.GridFontScaleFactor))]
        private void Configuration_FontChanged(object sender, Dinah.Core.PropertyChangedEventArgsEx e)
        {
            setFontScale((float)e.NewValue);
        }

        private readonly Style rowHeightStyle;
        private readonly Setter rowHeightSetter = new() { Property = DataGridCell.HeightProperty };

        private readonly Style fontSizeStyle;
        private readonly Setter fontSizeSetter = new() { Property = TextBlock.FontSizeProperty };

        private readonly Style fontSizeH1Style;
        private readonly Setter fontSizeH1Setter = new() { Property = TextBlock.FontSizeProperty };

        private readonly Style fontSizeH2Style;
        private readonly Setter fontSizeH2Setter = new() { Property = TextBlock.FontSizeProperty };

        private void setFontScale(double scaleFactor)
        {
            const double TextBlockFontSize = 11;
            const double H1FontSize = 14;
            const double H2FontSize = 12;

            fontSizeSetter.Value = TextBlockFontSize * scaleFactor;
            fontSizeH1Setter.Value = H1FontSize * scaleFactor;
            fontSizeH2Setter.Value = H2FontSize * scaleFactor;

            productsGrid.Styles.Remove(fontSizeStyle);
            productsGrid.Styles.Remove(fontSizeH1Style);
            productsGrid.Styles.Remove(fontSizeH2Style);
            productsGrid.Styles.Add(fontSizeStyle);
            productsGrid.Styles.Add(fontSizeH1Style);
            productsGrid.Styles.Add(fontSizeH2Style);
        }

        private void setGridScale(double scaleFactor)
        {
            const float BaseRowHeight = 80;
            const float BaseLiberateWidth = 75;
            const float BaseCoverWidth = 80;

            foreach (var column in productsGrid.Columns)
            {
                switch (column.SortMemberPath)
                {
                    case nameof(IGridEntry.Liberate):
                        column.Width = new DataGridLength(BaseLiberateWidth * scaleFactor);
                        break;
                    case nameof(IGridEntry.Cover):
                        column.Width = new DataGridLength(BaseCoverWidth * scaleFactor);
                        break;
                }
            }
            rowHeightSetter.Value = BaseRowHeight * scaleFactor;
            productsGrid.Styles.Remove(rowHeightStyle);
            productsGrid.Styles.Add(rowHeightStyle);
        }

        #endregion

        #region Cell Context Menu

        public void ProductsGrid_CellContextMenuStripNeeded(object sender, DataGridCellContextMenuStripNeededEventArgs args)
        {
            var entry = args.GridEntry;
            var ctx = new GridContextMenu(entry, '_');

            if (args.Column.SortMemberPath is not "Liberate" and not "Cover")
            {
                args.ContextMenuItems.Add(new MenuItem
                {
                    Header = ctx.CopyCellText,
                    Command = ReactiveCommand.CreateFromTask(() => App.MainWindow.Clipboard.SetTextAsync(args.CellClipboardContents))
                });
                args.ContextMenuItems.Add(new Separator());
            }

            #region Liberate all Episodes

            if (entry.Liberate.IsSeries)
            {
                args.ContextMenuItems.Add(new MenuItem()
                {
                    Header = ctx.LiberateEpisodesText,
                    IsEnabled = ctx.LiberateEpisodesEnabled,
                    Command = ReactiveCommand.Create(() => LiberateSeriesClicked?.Invoke(this, (ISeriesEntry)entry))
                });
            }

            #endregion
            #region Set Download status to Downloaded

            args.ContextMenuItems.Add(new MenuItem()
            {
                Header = ctx.SetDownloadedText,
                IsEnabled = ctx.SetDownloadedEnabled,
                Command = ReactiveCommand.Create(ctx.SetDownloaded)
            });

            #endregion
            #region Set Download status to Not Downloaded

            args.ContextMenuItems.Add(new MenuItem()
            {
                Header = ctx.SetNotDownloadedText,
                IsEnabled = ctx.SetNotDownloadedEnabled,
                Command = ReactiveCommand.Create(ctx.SetNotDownloaded)
            });

            #endregion
            #region Remove from library

            args.ContextMenuItems.Add(new MenuItem
            {
                Header = ctx.RemoveText,
                Command = ReactiveCommand.CreateFromTask(ctx.RemoveAsync)
            });

            #endregion

            if (!entry.Liberate.IsSeries)
            {
                #region Locate file

                args.ContextMenuItems.Add(new MenuItem
                {
                    Header = ctx.LocateFileText,
                    Command = ReactiveCommand.CreateFromTask(async () =>
                    {
                        try
                        {
                            var window = this.GetParentWindow();

                            var openFileDialogOptions = new FilePickerOpenOptions
                            {
                                Title = ctx.LocateFileDialogTitle,
                                AllowMultiple = false,
                                SuggestedStartLocation = await window.StorageProvider.TryGetFolderFromPathAsync(Configuration.Instance.Books.PathWithoutPrefix),
                                FileTypeFilter = new FilePickerFileType[]
                                {
                                    new("All files (*.*)") { Patterns = new[] { "*" } },
                                }
                            };

                            var selectedFiles = await window.StorageProvider.OpenFilePickerAsync(openFileDialogOptions);
                            var selectedFile = selectedFiles.SingleOrDefault()?.TryGetLocalPath();

                            if (selectedFile is not null)
                                FilePathCache.Insert(entry.AudibleProductId, selectedFile);
                        }
                        catch (Exception ex)
                        {
                            await MessageBox.ShowAdminAlert(null, ctx.LocateFileErrorMessage, ctx.LocateFileErrorMessage, ex);
                        }
                    })
                });

                #endregion
                #region Convert to Mp3
                args.ContextMenuItems.Add(new MenuItem
                {
                    Header = ctx.ConvertToMp3Text,
                    IsEnabled = ctx.ConvertToMp3Enabled,
                    Command = ReactiveCommand.Create(() => ConvertToMp3Clicked?.Invoke(this, entry.LibraryBook))
                });

                #endregion
            }

            #region Force Re-Download
            if (!entry.Liberate.IsSeries)
            {
                args.ContextMenuItems.Add(new MenuItem()
                {
                    Header = ctx.ReDownloadText,
                    IsEnabled = ctx.ReDownloadEnabled,
                    Command = ReactiveCommand.Create(() =>
                    {
                        //No need to persist this change. It only needs to last long for the file to start downloading
                        entry.Book.UserDefinedItem.BookStatus = LiberatedStatus.NotLiberated;
                        LiberateClicked?.Invoke(this, entry.LibraryBook);
                    })
                });
            }
            #endregion

            args.ContextMenuItems.Add(new Separator());

            #region Edit Templates
            async Task editTemplate<T>(LibraryBook libraryBook, string existingTemplate, Action<string> setNewTemplate)
                where T : Templates, LibationFileManager.ITemplate, new()
            {
                var template = ctx.CreateTemplateEditor<T>(libraryBook, existingTemplate);
                var form = new EditTemplateDialog(template);
                if (await form.ShowDialog<DialogResult>(this.GetParentWindow()) == DialogResult.OK)
                {
                    setNewTemplate(template.EditingTemplate.TemplateText);
                }
            }

            if (!entry.Liberate.IsSeries)
            {
                args.ContextMenuItems.Add(new MenuItem
                {
                    Header = ctx.EditTemplatesText,
                    ItemsSource = new[]
                    {
                        new MenuItem
                        {
                            Header = ctx.FolderTemplateText,
                            Command = ReactiveCommand.CreateFromTask(() => editTemplate<Templates.FolderTemplate>(entry.LibraryBook, Configuration.Instance.FolderTemplate, t => Configuration.Instance.FolderTemplate = t))
                        },
                        new MenuItem
                        {
                            Header = ctx.FileTemplateText,
                            Command = ReactiveCommand.CreateFromTask(() => editTemplate<Templates.FileTemplate>(entry.LibraryBook, Configuration.Instance.FileTemplate, t => Configuration.Instance.FileTemplate = t))
                        },
                        new MenuItem
                        {
                            Header = ctx.MultipartTemplateText,
                            Command = ReactiveCommand.CreateFromTask(() => editTemplate<Templates.ChapterFileTemplate>(entry.LibraryBook, Configuration.Instance.ChapterFileTemplate, t => Configuration.Instance.ChapterFileTemplate = t))
                        }
                    }
                });
                args.ContextMenuItems.Add(new Separator());
            }

            #endregion

            #region View Bookmarks/Clips

            if (!entry.Liberate.IsSeries)
            {
                args.ContextMenuItems.Add(new MenuItem
                {
                    Header = ctx.ViewBookmarksText,
                    Command = ReactiveCommand.CreateFromTask(() => new BookRecordsDialog(entry.LibraryBook).ShowDialog(VisualRoot as Window))
                });
            }

            #endregion
            #region View All Series

            if (entry.Book.SeriesLink.Any())
            {
                args.ContextMenuItems.Add(new MenuItem
                {
                    Header = ctx.ViewSeriesText,
                    Command = ReactiveCommand.Create(() => new SeriesViewDialog(entry.LibraryBook).Show())
                });
            }

            #endregion
        }

        #endregion

        #region Column Customizations

        private void Configure_ColumnCustomization()
        {
            if (Design.IsDesignMode) return;

            productsGrid.ColumnDisplayIndexChanged += ProductsGrid_ColumnDisplayIndexChanged;

            var config = Configuration.Instance;
            var gridColumnsVisibilities = config.GridColumnsVisibilities;
            var displayIndices = config.GridColumnsDisplayIndices;

            var contextMenu = new ContextMenu();
            contextMenu.Closed += ContextMenu_MenuClosed;
            contextMenu.Opening += ContextMenu_ContextMenuOpening;
            List<Control> menuItems = new();
            contextMenu.ItemsSource = menuItems;

            menuItems.Add(new MenuItem { Header = "Show / Hide Columns" });
            menuItems.Add(new MenuItem { Header = "-" });

            var HeaderCell_PI = typeof(DataGridColumn).GetProperty("HeaderCell", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            foreach (var column in productsGrid.Columns)
            {
                var itemName = column.SortMemberPath;

                if (itemName == nameof(IGridEntry.Remove) || itemName == null)
                    continue;

                menuItems.Add
                    (
                        new MenuItem
                        {
                            Header = ((string)column.Header)?.Replace('\n', ' '),
                            Tag = column,
                            Icon = new CheckBox(),
                        }
                    );

                var headercell = HeaderCell_PI.GetValue(column) as DataGridColumnHeader;
                headercell.ContextMenu = contextMenu;

                column.IsVisible = itemName != null ? gridColumnsVisibilities.GetValueOrDefault(itemName, true) : true;
            }

            //We must set DisplayIndex properties in ascending order
            foreach (var itemName in displayIndices.OrderBy(i => i.Value).Select(i => i.Key))
            {
                if (!productsGrid.Columns.Any(c => c.SortMemberPath == itemName))
                    continue;

                var column = productsGrid.Columns
                    .Single(c => c.SortMemberPath == itemName);

                column.DisplayIndex = displayIndices.GetValueOrDefault(itemName, productsGrid.Columns.IndexOf(column));
            }
        }

        private void ContextMenu_ContextMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var contextMenu = sender as ContextMenu;
            foreach (var mi in contextMenu.Items.OfType<MenuItem>())
            {
                if (mi.Tag is DataGridColumn column)
                {
                    var cbox = mi.Icon as CheckBox;
                    cbox.IsChecked = column.IsVisible;
                }
            }
        }

        private void ContextMenu_MenuClosed(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var contextMenu = sender as ContextMenu;
            var config = Configuration.Instance;
            var dictionary = config.GridColumnsVisibilities;

            foreach (var mi in contextMenu.Items.OfType<MenuItem>())
            {
                if (mi.Tag is DataGridColumn column)
                {
                    var cbox = mi.Icon as CheckBox;
                    column.IsVisible = cbox.IsChecked == true;
                    dictionary[column.SortMemberPath] = cbox.IsChecked == true;
                }
            }

            //If all columns are hidden, register the context menu on the grid so users can unhide.
            if (!productsGrid.Columns.Any(c => c.IsVisible))
                productsGrid.ContextMenu = contextMenu;
            else
                productsGrid.ContextMenu = null;

            config.GridColumnsVisibilities = dictionary;
        }

        private void ProductsGrid_ColumnDisplayIndexChanged(object sender, DataGridColumnEventArgs e)
        {
            var config = Configuration.Instance;

            var dictionary = config.GridColumnsDisplayIndices;
            dictionary[e.Column.SortMemberPath] = e.Column.DisplayIndex;
            config.GridColumnsDisplayIndices = dictionary;
        }

        #endregion

        #region Button Click Handlers

        public async void LiberateButton_Click(object sender, EventArgs e)
        {
            var button = sender as LiberateStatusButton;

            if (button.DataContext is ISeriesEntry sEntry)
            {
                await _viewModel.ToggleSeriesExpanded(sEntry);

                //Expanding and collapsing reset the list, which will cause focus to shift
                //to the topright cell. Reset focus onto the clicked button's cell.
                button.Focus();
            }
            else if (button.DataContext is ILibraryBookEntry lbEntry)
            {
                LiberateClicked?.Invoke(this, lbEntry.LibraryBook);
            }
        }

        public void CloseImageDisplay()
        {
            if (imageDisplayDialog is not null && imageDisplayDialog.IsVisible)
                imageDisplayDialog.Close();
        }

        public void Version_DoubleClick(object sender, Avalonia.Input.TappedEventArgs args)
        {
            if (sender is Control panel && panel.DataContext is ILibraryBookEntry lbe && lbe.LastDownload.IsValid)
                lbe.LastDownload.OpenReleaseUrl();
        }

        public void Cover_Click(object sender, Avalonia.Input.TappedEventArgs args)
        {
            if (sender is not Image tblock || tblock.DataContext is not IGridEntry gEntry)
                return;

            if (imageDisplayDialog is null || !imageDisplayDialog.IsVisible)
            {
                imageDisplayDialog = new ImageDisplayDialog();
            }

            var picDef = new PictureDefinition(gEntry.LibraryBook.Book.PictureLarge ?? gEntry.LibraryBook.Book.PictureId, PictureSize.Native);

            void PictureCached(object sender, PictureCachedEventArgs e)
            {
                if (e.Definition.PictureId == picDef.PictureId)
                    imageDisplayDialog.SetCoverBytes(e.Picture);

                PictureStorage.PictureCached -= PictureCached;
            }

            PictureStorage.PictureCached += PictureCached;
            (bool isDefault, byte[] initialImageBts) = PictureStorage.GetPicture(picDef);


            var windowTitle = $"{gEntry.Title} - Cover";


            imageDisplayDialog.BookSaveDirectory = AudibleFileStorage.Audio.GetDestinationDirectory(gEntry.LibraryBook);
            imageDisplayDialog.PictureFileName = System.IO.Path.GetFileName(AudibleFileStorage.Audio.GetBooksDirectoryFilename(gEntry.LibraryBook, ".jpg"));
            imageDisplayDialog.Title = windowTitle;
            imageDisplayDialog.SetCoverBytes(initialImageBts);

            if (!isDefault)
                PictureStorage.PictureCached -= PictureCached;

            if (imageDisplayDialog.IsVisible)
                imageDisplayDialog.Activate();
            else
                imageDisplayDialog.Show();
        }

        public void Description_Click(object sender, Avalonia.Input.TappedEventArgs args)
        {
            if (sender is Control tblock && tblock.DataContext is IGridEntry gEntry)
            {
                var pt = tblock.PointToScreen(tblock.Bounds.TopRight);
                var displayWindow = new DescriptionDisplayDialog
                {
                    SpawnLocation = new Point(pt.X, pt.Y),
                    DescriptionText = gEntry.Description,
                };

                void CloseWindow(object o, DataGridRowEventArgs e)
                {
                    displayWindow.Close();
                }
                productsGrid.LoadingRow += CloseWindow;
                displayWindow.Closing += (_, _) =>
                {
                    productsGrid.LoadingRow -= CloseWindow;
                };

                displayWindow.Show();
            }
        }

        BookDetailsDialog bookDetailsForm;

        public void OnTagsButtonClick(object sender, Avalonia.Interactivity.RoutedEventArgs args)
        {
            var button = args.Source as Button;

            if (button.DataContext is ILibraryBookEntry lbEntry && VisualRoot is Window window)
            {
                if (bookDetailsForm is null || !bookDetailsForm.IsVisible)
                {
                    bookDetailsForm = new BookDetailsDialog(lbEntry.LibraryBook);
                    bookDetailsForm.Show(window);
                }
                else
                    bookDetailsForm.LibraryBook = lbEntry.LibraryBook;
            }
        }

        private async void AddToPlaylistButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var gridEntry = (IGridEntry)((Button)sender).DataContext;
            if (gridEntry is ISeriesEntry se)
            {
                foreach (ILibraryBookEntry child in se.Children) 
                    await _playerViewModel.AddToPlaylist(child);
            }
            else if (gridEntry is ILibraryBookEntry book)
                await _playerViewModel.AddToPlaylist(book);
            else
                Serilog.Log.Logger.TryLogError($"Cannot add item type {gridEntry.GetType().Name} to playlist");
        }
        #endregion
    }
}

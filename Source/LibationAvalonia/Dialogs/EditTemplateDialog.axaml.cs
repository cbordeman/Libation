﻿using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Dinah.Core;
using LibationFileManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using Avalonia.Controls.Documents;
using Avalonia.Collections;
using Avalonia.Controls;

namespace LibationAvalonia.Dialogs
{
	public partial class EditTemplateDialog : DialogWindow
	{
		// final value. post-validity check
		public string TemplateText { get; private set; }

		private EditTemplateViewModel _viewModel;

		public EditTemplateDialog()
		{
			AvaloniaXamlLoader.Load(this);
			userEditTbox = this.FindControl<TextBox>(nameof(userEditTbox));
			if (Design.IsDesignMode)
			{
				_ = Configuration.Instance.LibationFiles;
				_viewModel = new(Configuration.Instance, Templates.File);
				_viewModel.resetTextBox(_viewModel.Template.DefaultTemplate);
				Title = $"Edit {_viewModel.Template.Name}";
				DataContext = _viewModel;
			}
		}

		public EditTemplateDialog(Templates template, string inputTemplateText) : this()
		{
			ArgumentValidator.EnsureNotNull(template, nameof(template));

			_viewModel = new EditTemplateViewModel(Configuration.Instance, template);
			_viewModel.resetTextBox(inputTemplateText);
			Title = $"Edit {template.Name}";
			DataContext = _viewModel;
		}


		public void EditTemplateViewModel_DoubleTapped(object sender, Avalonia.Input.TappedEventArgs e)
		{
			var dataGrid = sender as DataGrid;

			var item = (dataGrid.SelectedItem as Tuple<string, string, string>).Item3;
			if (string.IsNullOrWhiteSpace(item)) return;

			var text = userEditTbox.Text;

			userEditTbox.Text = text.Insert(Math.Min(Math.Max(0, userEditTbox.CaretIndex), text.Length), item);
			userEditTbox.CaretIndex += item.Length;
		}

		protected override async Task SaveAndCloseAsync()
		{
			if (!await _viewModel.Validate())
				return;

			TemplateText = _viewModel.workingTemplateText;
			await base.SaveAndCloseAsync();
		}

		public async void SaveButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
			=> await SaveAndCloseAsync();

		public void ResetButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
			=> _viewModel.resetTextBox(_viewModel.Template.DefaultTemplate);

		private class EditTemplateViewModel : ViewModels.ViewModelBase
		{
			private readonly Configuration config;
			public FontFamily FontFamily { get; } = FontManager.Current.DefaultFontFamilyName;
			public InlineCollection Inlines { get; } = new();
			public Templates Template { get; }
			public EditTemplateViewModel(Configuration configuration, Templates templates)
			{
				config = configuration;
				Template = templates;
				Description = templates.Description;
				ListItems
				= new AvaloniaList<Tuple<string, string, string>>(
					Template
					.GetTemplateTags()
					.Select(
						t => new Tuple<string, string, string>(
							$"<{t.TagName.Replace("->", "-\x200C>").Replace("<-", "<\x200C-")}>",
							t.Description,
							t.DefaultValue)
						)
					);

			}

			// hold the work-in-progress value. not guaranteed to be valid
			private string _userTemplateText;
			public string UserTemplateText
			{
				get => _userTemplateText;
				set
				{
					this.RaiseAndSetIfChanged(ref _userTemplateText, value);
					templateTb_TextChanged();
				}
			}

			public string workingTemplateText => Template.Sanitize(UserTemplateText, Configuration.Instance.ReplacementCharacters);
			private string _warningText;
			public string WarningText { get => _warningText; set => this.RaiseAndSetIfChanged(ref _warningText, value); }

			public string Description { get; }

			public AvaloniaList<Tuple<string, string, string>> ListItems { get; set; }

			public void resetTextBox(string value) => UserTemplateText = value;

			public async Task<bool> Validate()
			{
				if (Template.IsValid(workingTemplateText))
					return true;
				var errors = Template
					.GetErrors(workingTemplateText)
					.Select(err => $"- {err}")
					.Aggregate((a, b) => $"{a}\r\n{b}");
				await MessageBox.Show($"This template text is not valid. Errors:\r\n{errors}", "Invalid", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}

			private void templateTb_TextChanged()
			{
				var isChapterTitle = Template == Templates.ChapterTitle;
				var isFolder = Template == Templates.Folder;

				var libraryBookDto = new LibraryBookDto
				{
					Account = "my account",
					DateAdded = new DateTime(2022, 6, 9, 0, 0, 0),
					DatePublished = new DateTime(2017, 2, 27, 0, 0, 0),
					AudibleProductId = "123456789",
					Title = "A Study in Scarlet: A Sherlock Holmes Novel",
					Locale = "us",
					YearPublished = 2017,
					Authors = new List<string> { "Arthur Conan Doyle", "Stephen Fry - introductions" },
					Narrators = new List<string> { "Stephen Fry" },
					SeriesName = "Sherlock Holmes",
					SeriesNumber = "1",
					BitRate = 128,
					SampleRate = 44100,
					Channels = 2
				};
				var chapterName = "A Flight for Life";
				var chapterNumber = 4;
				var chaptersTotal = 10;

				var partFileProperties = new AaxDecrypter.MultiConvertFileProperties()
				{
					OutputFileName = "",
					PartsPosition = chapterNumber,
					PartsTotal = chaptersTotal,
					Title = chapterName
				};

				/*
				* Path must be rooted for windows to allow long file paths. This is
				* only necessary for folder templates because they may contain several
				* subdirectories. Without rooting, we won't be allowed to create a
				* relative path longer than MAX_PATH.
				*/

				var books = config.Books;
				var folder = Templates.Folder.GetPortionFilename(
					libraryBookDto,
					Path.Combine(books, isFolder ? workingTemplateText : config.FolderTemplate), "");

				folder = Path.GetRelativePath(books, folder);

				var file
				= Template == Templates.ChapterFile
				? Templates.ChapterFile.GetPortionFilename(
					libraryBookDto,
					workingTemplateText,
					partFileProperties,
					"")
				: Templates.File.GetPortionFilename(
					libraryBookDto,
					isFolder ? config.FileTemplate : workingTemplateText, "");
				var ext = config.DecryptToLossy ? "mp3" : "m4b";

				var chapterTitle = Templates.ChapterTitle.GetPortionTitle(libraryBookDto, workingTemplateText, partFileProperties);

				const char ZERO_WIDTH_SPACE = '\u200B';
				var sing = $"{Path.DirectorySeparatorChar}";

				// result: can wrap long paths. eg:
				// |-- LINE WRAP BOUNDARIES --|
				// \books\author with a very     <= normal line break on space between words
				// long name\narrator narrator   
				// \title                        <= line break on the zero-with space we added before slashes
				string slashWrap(string val) => val.Replace(sing, $"{ZERO_WIDTH_SPACE}{sing}");

				WarningText
					= !Template.HasWarnings(workingTemplateText)
					? ""
					: "Warning:\r\n" +
						Template
						.GetWarnings(workingTemplateText)
						.Select(err => $"- {err}")
						.Aggregate((a, b) => $"{a}\r\n{b}");

				var bold = FontWeight.Bold;
				var reg = FontWeight.Normal;

				Inlines.Clear();

				if (isChapterTitle)
				{
					Inlines.Add(new Run(chapterTitle) { FontWeight = bold });
					return;
				}

				Inlines.Add(new Run(slashWrap(books)) { FontWeight = reg });
				Inlines.Add(new Run(sing) { FontWeight = reg });

				Inlines.Add(new Run(slashWrap(folder)) { FontWeight = isFolder ? bold : reg });

				Inlines.Add(new Run(sing));

				Inlines.Add(new Run(slashWrap(file)) { FontWeight = isFolder ? reg : bold });

				Inlines.Add(new Run($".{ext}"));
			}
		}
	}
}

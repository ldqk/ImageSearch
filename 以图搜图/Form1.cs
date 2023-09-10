using Masuit.Tools.Logging;
using Masuit.Tools.Media;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Masuit.Tools.Files.FileDetector;
using Masuit.Tools.Systems;
using Image = SixLabors.ImageSharp.Image;
using ImageFormat = System.Drawing.Imaging.ImageFormat;
using System.Runtime.InteropServices;

namespace 以图搜图
{
	public partial class Form1 : Form
	{
		public Form1()
		{
			InitializeComponent();
			CheckForIllegalCrossThreadCalls = false;
		}

		private void btnDirectory_Click(object sender, EventArgs e)
		{
			using var dialog = new FolderBrowserDialog();
			var result = dialog.ShowDialog();
			if (result == DialogResult.OK)
			{
				txtDirectory.Text = dialog.SelectedPath;
			}
		}

		private void btnPic_Click(object sender, EventArgs e)
		{
			using var dialog = new OpenFileDialog();
			if (dialog.ShowDialog() == DialogResult.OK)
			{
				txtPic.Text = dialog.FileName;
			}
		}

		private ConcurrentDictionary<string, ulong[]> _index = new();

		public bool IndexRunning { get; set; }

		private async void btnIndex_Click(object sender, EventArgs e)
		{
			if (IndexRunning)
			{
				IndexRunning = false;
				btnIndex.Text = "更新索引";
				return;
			}

			if (string.IsNullOrEmpty(txtDirectory.Text))
			{
				MessageBox.Show("请先选择文件夹");
				return;
			}

			IndexRunning = true;
			btnIndex.Text = "停止索引";
			cbRemoveInvalidIndex.Hide();
			var imageHasher = new ImageHasher(new ImageSharpTransformer());
			int? filesCount = null;
			Task.Run(() => filesCount = Directory.EnumerateFiles(txtDirectory.Text, "*", SearchOption.AllDirectories).Except(_index.Keys).Count(s => Regex.IsMatch(s, "(jpg|png|bmp)$", RegexOptions.IgnoreCase))).ConfigureAwait(false);
			var local = new ThreadLocal<int>(true);
			await Task.Run(() =>
			{
				var sw = Stopwatch.StartNew();
				long size = 0;
				Directory.EnumerateFiles(txtDirectory.Text, "*", SearchOption.AllDirectories).Except(_index.Keys).Where(s => Regex.IsMatch(s, "(jpg|png|bmp)$", RegexOptions.IgnoreCase)).Chunk(Environment.ProcessorCount * 2).AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount * 2).ForAll(g =>
				 {
					 foreach (var s in g)
					 {
						 if (IndexRunning)
						 {
							 if (lblProcess.InvokeRequired)
							 {
								 local.Value++;
								 lblProcess.Invoke(() => lblProcess.Text = $"{local.Values.Sum()}/{filesCount}");
							 }
							 try
							 {
								 _index.GetOrAdd(s, _ => imageHasher.DifferenceHash256(s));
								 size += new FileInfo(s).Length;
							 }
							 catch
							 {
								 LogManager.Info(s + "格式不正确");
							 }
						 }
						 else
						 {
							 break;
						 }
					 }
				 });
				lbSpeed.Text = $"索引速度: {Math.Round(local.Values.Sum() * 1.0 / sw.Elapsed.TotalSeconds)} items/s({size * 1f / 1048576 / sw.Elapsed.TotalSeconds:N}MB/s)";
				if (cbRemoveInvalidIndex.Checked)
				{
					foreach (var (key, _) in _index.AsParallel().WithDegreeOfParallelism(32).Where(s => !File.Exists(s.Key)))
					{
						_index.TryRemove(key, out _);
					}
				}

				lbIndexCount.Text = _index.Count + "文件";
				cbRemoveInvalidIndex.Show();
				var json = JsonSerializer.Serialize(_index);
				File.WriteAllText("index.json", json, Encoding.UTF8);
				MessageBox.Show("索引创建完成，耗时：" + sw.Elapsed.TotalSeconds + "s");
			}).ConfigureAwait(false);
			IndexRunning = false;
			btnIndex.Text = "更新索引";
		}

		private void btnSearch_Click(object sender, EventArgs e)
		{
			if (string.IsNullOrEmpty(txtPic.Text))
			{
				MessageBox.Show("请先选择图片");
				return;
			}

			if (_index.Count == 0)
			{
				MessageBox.Show("当前没有任何索引，请先添加文件夹创建索引后再搜索");
				return;
			}

			if (!new FileInfo(txtPic.Text).DetectFiletype().MimeType.StartsWith("image"))
			{
				MessageBox.Show("不是图像文件，无法检索");
				return;
			}

			var sim = (float)numLike.Value / 100;
			var hasher = new ImageHasher();
			var sw = Stopwatch.StartNew();
			var hash = hasher.DifferenceHash256(txtPic.Text);
			var hashs = new ConcurrentBag<ulong[]> { hash };
			using (var image = Image.Load<Rgba32>(txtPic.Text))
			{
				var actions = new List<Action>();
				if (cbRotate.Checked)
				{
					actions.Add(() =>
					{
						using var clone = image.Clone(c => c.Rotate(90));
						var rotate90 = clone.DifferenceHash256();
						hashs.Add(rotate90);
					});
					actions.Add(() =>
					{
						using var clone = image.Clone(c => c.Rotate(180));
						var rotate180 = clone.DifferenceHash256();
						hashs.Add(rotate180);
					});
					actions.Add(() =>
					{
						using var clone = image.Clone(c => c.Rotate(270));
						var rotate270 = clone.DifferenceHash256();
						hashs.Add(rotate270);
					});
				}

				if (cbFlip.Checked)
				{
					actions.Add(() =>
					{
						using var clone = image.Clone(c => c.Flip(FlipMode.Horizontal));
						var flipH = clone.DifferenceHash256();
						hashs.Add(flipH);
					});
					actions.Add(() =>
					{
						using var clone = image.Clone(c => c.Flip(FlipMode.Vertical));
						var flipV = clone.DifferenceHash256();
						hashs.Add(flipV);
					});
				}
				Parallel.Invoke(actions.ToArray());
			}

			var list = _index.Select(x => new
			{
				路径 = x.Key,
				匹配度 = hashs.Select(h => ImageHasher.Compare(x.Value, h)).Max()
			}).Where(x => x.匹配度 >= sim).OrderByDescending(a => a.匹配度).ToList();
			lbElpased.Text = sw.ElapsedMilliseconds + "ms";
			if (list.Count > 0)
			{
				picSource.ImageLocation = txtPic.Text;
				picSource.Refresh();
			}

			dgvResult.DataSource = list;
		}

		private async void Form1_Load(object sender, EventArgs e)
		{
			lbIndexCount.Text = "正在加载索引...";
			if (File.Exists("index.json"))
			{
				_index = await JsonSerializer.DeserializeAsync<ConcurrentDictionary<string, ulong[]>>(File.OpenRead("index.json")).ConfigureAwait(false);
				lbIndexCount.Text = _index.Count + "文件";
			}
			else
			{
				lbIndexCount.Text = "请先创建索引";
			}
		}

		private void dgvResult_CellContentClick(object sender, DataGridViewCellEventArgs e)
		{
		}

		private void dgvResult_CellClick(object sender, DataGridViewCellEventArgs e)
		{
			picDest.ImageLocation = dgvResult.SelectedCells[0].OwningRow.Cells["路径"].Value.ToString();
		}

		private void picSource_LoadCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
		{
			lbSrcInfo.Text = $"分辨率：{picSource.Image.Width}x{picSource.Image.Height}，大小：{new FileInfo(picSource.ImageLocation).Length / 1024}KB";
		}

		private void picDest_LoadCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
		{
			lblDestInfo.Text = $"分辨率：{picDest.Image.Width}x{picDest.Image.Height}，大小：{new FileInfo(picDest.ImageLocation).Length / 1024}KB";
		}

		private void lblGithub_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			Process.Start(new ProcessStartInfo { FileName = lblGithub.Text, UseShellExecute = true });
		}

		private void txtDirectory_DragEnter(object sender, DragEventArgs e)
		{
			e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
		}

		private void txtDirectory_DragDrop(object sender, DragEventArgs e)
		{
			((TextBox)sender).Text = ((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();
		}

		private void txtPic_DragEnter(object sender, DragEventArgs e)
		{
			e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
		}

		private void txtPic_DragDrop(object sender, DragEventArgs e)
		{
			((TextBox)sender).Text = ((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();
		}

		private void buttonClipSearch_Click(object sender, EventArgs e)
		{
			if (Clipboard.ContainsFileDropList())
			{
				txtPic.Text = Clipboard.GetFileDropList()[0];
				btnSearch_Click(sender, e);
				return;
			}

			if (Clipboard.ContainsText())
			{
				var text = Clipboard.GetText();
				if (File.Exists(text))
				{
					btnSearch_Click(sender, e);
				}
				else
				{
					dgvResult.DataSource = null;
					picSource.Image = null;
					picSource.Refresh();
				}
				return;
			}

			if (Clipboard.ContainsImage())
			{
				using var sourceImage = Clipboard.GetImage();
				var filename = Path.Combine(Environment.GetEnvironmentVariable("temp"), SnowFlake.NewId);
				sourceImage.Save(filename, ImageFormat.Jpeg);
				var sim = (float)numLike.Value / 100;
				var hasher = new ImageHasher();
				var sw = Stopwatch.StartNew();
				var hash = hasher.DifferenceHash256(filename);
				var hashs = new ConcurrentBag<ulong[]> { hash };
				using (var image = Image.Load<Rgba32>(filename))
				{
					var actions = new List<Action>();
					if (cbRotate.Checked)
					{
						actions.Add(() =>
						{
							using var clone = image.Clone(c => c.Rotate(90));
							var rotate90 = clone.DifferenceHash256();
							hashs.Add(rotate90);
						});
						actions.Add(() =>
						{
							using var clone = image.Clone(c => c.Rotate(180));
							var rotate180 = clone.DifferenceHash256();
							hashs.Add(rotate180);
						});
						actions.Add(() =>
						{
							using var clone = image.Clone(c => c.Rotate(270));
							var rotate270 = clone.DifferenceHash256();
							hashs.Add(rotate270);
						});
					}

					if (cbFlip.Checked)
					{
						actions.Add(() =>
						{
							using var clone = image.Clone(c => c.Flip(FlipMode.Horizontal));
							var flipH = clone.DifferenceHash256();
							hashs.Add(flipH);
						});
						actions.Add(() =>
						{
							using var clone = image.Clone(c => c.Flip(FlipMode.Vertical));
							var flipV = clone.DifferenceHash256();
							hashs.Add(flipV);
						});
					}
					Parallel.Invoke(actions.ToArray());
				}

				var list = _index.Select(x => new
				{
					路径 = x.Key,
					匹配度 = hashs.Select(h => ImageHasher.Compare(x.Value, h)).Max()
				}).Where(x => x.匹配度 >= sim).OrderByDescending(a => a.匹配度).ToList();
				lbElpased.Text = sw.ElapsedMilliseconds + "ms";
				if (list.Count > 0)
				{
					picSource.ImageLocation = filename;
					picSource.Refresh();
				}

				dgvResult.DataSource = list;
				Task.Run(() =>
				{
					Thread.Sleep(1000);
					File.Delete(filename);
				}).ContinueWith(_ => 0).ConfigureAwait(false);
			}
		}

		private void dgvResult_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
		{
			if (e.Button == MouseButtons.Right && dgvResult.SelectedCells.Count > 0)
			{
				dgvContextMenuStrip.Show(MousePosition.X, MousePosition.Y);
				return;
			}

			dgvContextMenuStrip.Hide();
		}

		private void 打开所在文件夹_Click(object sender, EventArgs e)
		{
			ExplorerFile(dgvResult.SelectedCells[0].OwningRow.Cells["路径"].Value.ToString());
		}

		private void dgvResult_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
		{
			Process.Start(new ProcessStartInfo { FileName = dgvResult.SelectedCells[0].OwningRow.Cells["路径"].Value.ToString(), UseShellExecute = true });
		}

		/// <summary>
		/// 打开路径并定位文件...对于@"h:\Bleacher Report - Hardaway with the safe call ??.mp4"这样的，explorer.exe /select,d:xxx不认，用API整它
		/// </summary>
		[DllImport("shell32.dll", ExactSpelling = true)]
		private static extern void ILFree(IntPtr pidlList);

		[DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
		private static extern IntPtr ILCreateFromPathW(string pszPath);

		[DllImport("shell32.dll", ExactSpelling = true)]
		private static extern int SHOpenFolderAndSelectItems(IntPtr pidlList, uint cild, IntPtr children, uint dwFlags);

		public void ExplorerFile(string filePath)
		{
			if (!File.Exists(filePath))
			{
				return;
			}

			var pidlList = ILCreateFromPathW(filePath);
			if (pidlList != IntPtr.Zero)
			{
				try
				{
					Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(pidlList, 0, IntPtr.Zero, 0));
				}
				catch
				{
				}
				finally
				{
					ILFree(pidlList);
				}
			}
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (keyData == (Keys.Control | Keys.V))
			{
				buttonClipSearch_Click(null, null);
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}
	}
}

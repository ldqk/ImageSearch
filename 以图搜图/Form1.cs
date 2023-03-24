using Masuit.Tools.Logging;
using Masuit.Tools.Media;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Image = SixLabors.ImageSharp.Image;

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

		private async void btnIndex_Click(object sender, EventArgs e)
		{
			if (string.IsNullOrEmpty(txtDirectory.Text))
			{
				MessageBox.Show("请先选择文件夹");
				return;
			}

			cbRemoveInvalidIndex.Hide();
			var imageHasher = new ImageHasher(new ImageSharpTransformer());
			await Task.Run(() =>
			{
				var files = Directory.EnumerateFiles(txtDirectory.Text, "*", SearchOption.AllDirectories).Where(s => Regex.IsMatch(s, "(jpg|png|bmp)$", RegexOptions.IgnoreCase)).ToList();
				var sw = Stopwatch.StartNew();
				int pro = 1;
				files.Chunk(Environment.ProcessorCount * 2).AsParallel().ForAll(g =>
				{
					foreach (var s in g)
					{
						if (lblProcess.InvokeRequired)
						{
							lblProcess.Invoke(() => lblProcess.Text = pro++ + "/" + files.Count);
						}
						try
						{
							_index.GetOrAdd(s, _ => imageHasher.DifferenceHash256(s));
						}
						catch
						{
							LogManager.Info(s + "格式不正确");
						}
					}
				});
				lbSpeed.Text = "索引速度:" + Math.Round(pro * 1.0 / sw.Elapsed.TotalSeconds) + "/s";
				MessageBox.Show("索引创建完成，耗时：" + sw.Elapsed.TotalSeconds + "s");
				if (cbRemoveInvalidIndex.Checked)
				{
					_index.Keys.AsParallel().Where(s => !File.Exists(s)).ForAll(s => _index.TryRemove(s, out _));
				}

				lbIndexCount.Text = _index.Count + "文件";
				cbRemoveInvalidIndex.Show();
				var json = JsonSerializer.Serialize(_index);
				File.WriteAllText("index.json", json, Encoding.UTF8);
			}).ConfigureAwait(false);
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
						var rotate90 = image.Clone(c => c.Rotate(90)).DifferenceHash256();
						hashs.Add(rotate90);
					});
					actions.Add(() =>
					{
						var rotate180 = image.Clone(c => c.Rotate(180)).DifferenceHash256();
						hashs.Add(rotate180);
					});
					actions.Add(() =>
					{
						var rotate270 = image.Clone(c => c.Rotate(270)).DifferenceHash256();
						hashs.Add(rotate270);
					});
				}

				if (cbFlip.Checked)
				{
					actions.Add(() =>
					{
						var flipH = image.Clone(c => c.Flip(FlipMode.Horizontal)).DifferenceHash256();
						hashs.Add(flipH);
					});
					actions.Add(() =>
					{
						var flipV = image.Clone(c => c.Flip(FlipMode.Horizontal)).DifferenceHash256();
						hashs.Add(flipV);
					});
				}
				Parallel.Invoke(actions.ToArray());
			}

			var list = _index.AsParallel().Select(x => new
			{
				路径 = x.Key,
				匹配度 = hashs.Select(h => ImageHasher.Compare(x.Value, h)).ToArray()
			}).Where(x => x.匹配度.Any(f => f >= sim)).Select(a => new
			{
				a.路径,
				匹配度 = a.匹配度.Max()
			}).OrderByDescending(a => a.匹配度).ToList();
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
			if (File.Exists("index.json"))
			{
				_index = await JsonSerializer.DeserializeAsync<ConcurrentDictionary<string, ulong[]>>(File.OpenRead("index.json")).ConfigureAwait(false);
				lbIndexCount.Text = _index.Count + "文件";
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
	}
}

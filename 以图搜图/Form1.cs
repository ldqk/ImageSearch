using System;
using Masuit.Tools.Logging;
using Masuit.Tools.Media;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Masuit.Tools.Files.FileDetector;
using Masuit.Tools.Systems;
using Image = SixLabors.ImageSharp.Image;
using SharpCompress.Common;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

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
            Task.Run(() => filesCount = Directory.EnumerateFiles(txtDirectory.Text, "*", SearchOption.AllDirectories).Count(s => Regex.IsMatch(s, "(jpg|png|bmp)$", RegexOptions.IgnoreCase)));
            await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                int pro = 1;
                Directory.EnumerateFiles(txtDirectory.Text, "*", SearchOption.AllDirectories).Where(s => Regex.IsMatch(s, "(jpg|png|bmp)$", RegexOptions.IgnoreCase)).Chunk(Environment.ProcessorCount * 2).AsParallel().ForAll(g =>
                {
                    foreach (var s in g)
                    {
                        if (IndexRunning)
                        {
                            if (lblProcess.InvokeRequired)
                            {
                                lblProcess.Invoke(() => lblProcess.Text = pro++ + "/" + filesCount);
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
                        else
                        {
                            break;
                        }
                    }
                });
                lbSpeed.Text = "索引速度:" + Math.Round(pro * 1.0 / sw.Elapsed.TotalSeconds) + "/s";
                if (cbRemoveInvalidIndex.Checked)
                {
                    _index.Keys.AsParallel().Where(s => !File.Exists(s)).ForAll(s => _index.TryRemove(s, out _));
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
    }
}

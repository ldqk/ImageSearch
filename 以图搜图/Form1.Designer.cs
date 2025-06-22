namespace 以图搜图
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            label1 = new Label();
            txtDirectory = new TextBox();
            btnDirectory = new Button();
            btnPic = new Button();
            txtPic = new TextBox();
            label2 = new Label();
            btnSearch = new Button();
            btnIndex = new Button();
            picSource = new PictureBox();
            picDest = new PictureBox();
            label3 = new Label();
            dgvResult = new DataGridView();
            dgvContextMenuStrip = new ContextMenuStrip(components);
            打开所在文件夹 = new ToolStripMenuItem();
            删除 = new ToolStripMenuItem();
            删除到回收站ToolStripMenuItem = new ToolStripMenuItem();
            label4 = new Label();
            lbIndexCount = new Label();
            label5 = new Label();
            lbElpased = new Label();
            lblDestInfo = new Label();
            lbSrcInfo = new Label();
            lbSpeed = new Label();
            numLike = new NumericUpDown();
            cbRotate = new CheckBox();
            cbFlip = new CheckBox();
            label6 = new Label();
            lblProcess = new Label();
            cbRemoveInvalidIndex = new CheckBox();
            lblGithub = new LinkLabel();
            buttonClipSearch = new Button();
            ((System.ComponentModel.ISupportInitialize)picSource).BeginInit();
            ((System.ComponentModel.ISupportInitialize)picDest).BeginInit();
            ((System.ComponentModel.ISupportInitialize)dgvResult).BeginInit();
            dgvContextMenuStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numLike).BeginInit();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(14, 10);
            label1.Name = "label1";
            label1.Size = new Size(103, 19);
            label1.TabIndex = 0;
            label1.Text = "添加搜索文件夹:";
            // 
            // txtDirectory
            // 
            txtDirectory.AllowDrop = true;
            txtDirectory.Location = new Point(129, 7);
            txtDirectory.Name = "txtDirectory";
            txtDirectory.Size = new Size(637, 24);
            txtDirectory.TabIndex = 1;
            txtDirectory.DragDrop += txtDirectory_DragDrop;
            txtDirectory.DragEnter += txtDirectory_DragEnter;
            // 
            // btnDirectory
            // 
            btnDirectory.Location = new Point(774, 10);
            btnDirectory.Name = "btnDirectory";
            btnDirectory.Size = new Size(73, 26);
            btnDirectory.TabIndex = 2;
            btnDirectory.Text = "选择";
            btnDirectory.UseVisualStyleBackColor = true;
            btnDirectory.Click += btnDirectory_Click;
            // 
            // btnPic
            // 
            btnPic.Location = new Point(774, 44);
            btnPic.Name = "btnPic";
            btnPic.Size = new Size(73, 26);
            btnPic.TabIndex = 5;
            btnPic.Text = "选择";
            btnPic.UseVisualStyleBackColor = true;
            btnPic.Click += btnPic_Click;
            // 
            // txtPic
            // 
            txtPic.AllowDrop = true;
            txtPic.Location = new Point(129, 44);
            txtPic.Name = "txtPic";
            txtPic.Size = new Size(637, 24);
            txtPic.TabIndex = 0;
            txtPic.DragDrop += txtPic_DragDrop;
            txtPic.DragEnter += txtPic_DragEnter;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(15, 47);
            label2.Name = "label2";
            label2.Size = new Size(103, 19);
            label2.TabIndex = 3;
            label2.Text = "用于检索的图片:";
            // 
            // btnSearch
            // 
            btnSearch.Location = new Point(424, 74);
            btnSearch.Name = "btnSearch";
            btnSearch.Size = new Size(58, 26);
            btnSearch.TabIndex = 6;
            btnSearch.Text = "搜索";
            btnSearch.UseVisualStyleBackColor = true;
            btnSearch.Click += btnSearch_Click;
            // 
            // btnIndex
            // 
            btnIndex.Location = new Point(854, 10);
            btnIndex.Name = "btnIndex";
            btnIndex.Size = new Size(86, 26);
            btnIndex.TabIndex = 7;
            btnIndex.Text = "更新索引";
            btnIndex.UseVisualStyleBackColor = true;
            btnIndex.Click += btnIndex_Click;
            // 
            // picSource
            // 
            picSource.BorderStyle = BorderStyle.FixedSingle;
            picSource.Location = new Point(975, 104);
            picSource.Name = "picSource";
            picSource.Size = new Size(311, 185);
            picSource.SizeMode = PictureBoxSizeMode.Zoom;
            picSource.TabIndex = 9;
            picSource.TabStop = false;
            picSource.Tag = "1";
            picSource.LoadCompleted += picSource_LoadCompleted;
            picSource.Click += picSource_Click;
            picSource.DoubleClick += picSource_DoubleClick;
            // 
            // picDest
            // 
            picDest.Location = new Point(975, 322);
            picDest.Name = "picDest";
            picDest.Size = new Size(311, 213);
            picDest.SizeMode = PictureBoxSizeMode.Zoom;
            picDest.TabIndex = 10;
            picDest.TabStop = false;
            picDest.LoadCompleted += picDest_LoadCompleted;
            picDest.DoubleClick += picDest_Click;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(35, 78);
            label3.Name = "label3";
            label3.Size = new Size(87, 19);
            label3.TabIndex = 11;
            label3.Text = "查找相似度：";
            // 
            // dgvResult
            // 
            dgvResult.AllowUserToAddRows = false;
            dgvResult.AllowUserToDeleteRows = false;
            dgvResult.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            dgvResult.BackgroundColor = SystemColors.Control;
            dgvResult.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvResult.Location = new Point(14, 104);
            dgvResult.Name = "dgvResult";
            dgvResult.ReadOnly = true;
            dgvResult.Size = new Size(954, 511);
            dgvResult.TabIndex = 13;
            dgvResult.CellClick += dgvResult_CellClick;
            dgvResult.CellContentClick += dgvResult_CellContentClick;
            dgvResult.CellDoubleClick += dgvResult_CellDoubleClick;
            dgvResult.CellMouseDown += dgvResult_CellMouseDown;
            dgvResult.KeyDown += dgvResult_KeyDown;
            dgvResult.KeyUp += dgvResult_KeyUp;
            // 
            // dgvContextMenuStrip
            // 
            dgvContextMenuStrip.Items.AddRange(new ToolStripItem[] { 打开所在文件夹, 删除, 删除到回收站ToolStripMenuItem });
            dgvContextMenuStrip.Name = "dgvContextMenuStrip";
            dgvContextMenuStrip.Size = new Size(243, 76);
            // 
            // 打开所在文件夹
            // 
            打开所在文件夹.Name = "打开所在文件夹";
            打开所在文件夹.Size = new Size(242, 24);
            打开所在文件夹.Text = "打开所在文件夹(Ctrl+O)";
            打开所在文件夹.Click += 打开所在文件夹_Click;
            // 
            // 删除
            // 
            删除.Name = "删除";
            删除.Size = new Size(242, 24);
            删除.Text = "删除(Delete)";
            删除.Click += 删除_Click;
            // 
            // 删除到回收站ToolStripMenuItem
            // 
            删除到回收站ToolStripMenuItem.Name = "删除到回收站ToolStripMenuItem";
            删除到回收站ToolStripMenuItem.Size = new Size(242, 24);
            删除到回收站ToolStripMenuItem.Text = "删除到回收站(Shift+Delete)";
            删除到回收站ToolStripMenuItem.Click += 删除到回收站ToolStripMenuItem_Click;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(862, 47);
            label4.Name = "label4";
            label4.Size = new Size(87, 19);
            label4.TabIndex = 14;
            label4.Text = "索引总数量：";
            // 
            // lbIndexCount
            // 
            lbIndexCount.AutoSize = true;
            lbIndexCount.Location = new Point(959, 48);
            lbIndexCount.Name = "lbIndexCount";
            lbIndexCount.Size = new Size(0, 19);
            lbIndexCount.TabIndex = 15;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(605, 78);
            label5.Name = "label5";
            label5.Size = new Size(74, 19);
            label5.TabIndex = 16;
            label5.Text = "搜索耗时：";
            // 
            // lbElpased
            // 
            lbElpased.AutoSize = true;
            lbElpased.Location = new Point(689, 77);
            lbElpased.Name = "lbElpased";
            lbElpased.Size = new Size(0, 19);
            lbElpased.TabIndex = 17;
            // 
            // lblDestInfo
            // 
            lblDestInfo.AutoSize = true;
            lblDestInfo.Location = new Point(975, 541);
            lblDestInfo.Name = "lblDestInfo";
            lblDestInfo.Size = new Size(0, 19);
            lblDestInfo.TabIndex = 18;
            // 
            // lbSrcInfo
            // 
            lbSrcInfo.AutoSize = true;
            lbSrcInfo.Location = new Point(979, 293);
            lbSrcInfo.Name = "lbSrcInfo";
            lbSrcInfo.Size = new Size(0, 19);
            lbSrcInfo.TabIndex = 19;
            // 
            // lbSpeed
            // 
            lbSpeed.AutoSize = true;
            lbSpeed.Location = new Point(1065, 15);
            lbSpeed.Name = "lbSpeed";
            lbSpeed.Size = new Size(17, 19);
            lbSpeed.TabIndex = 20;
            lbSpeed.Text = "  ";
            // 
            // numLike
            // 
            numLike.Location = new Point(130, 75);
            numLike.Minimum = new decimal(new int[] { 70, 0, 0, 0 });
            numLike.Name = "numLike";
            numLike.Size = new Size(51, 24);
            numLike.TabIndex = 21;
            numLike.Value = new decimal(new int[] { 80, 0, 0, 0 });
            // 
            // cbRotate
            // 
            cbRotate.AutoSize = true;
            cbRotate.Checked = true;
            cbRotate.CheckState = CheckState.Checked;
            cbRotate.Location = new Point(195, 77);
            cbRotate.Name = "cbRotate";
            cbRotate.Size = new Size(93, 23);
            cbRotate.TabIndex = 22;
            cbRotate.Text = "查找已旋转";
            cbRotate.UseVisualStyleBackColor = true;
            // 
            // cbFlip
            // 
            cbFlip.AutoSize = true;
            cbFlip.Location = new Point(314, 77);
            cbFlip.Name = "cbFlip";
            cbFlip.Size = new Size(93, 23);
            cbFlip.TabIndex = 23;
            cbFlip.Text = "查找已翻转";
            cbFlip.UseVisualStyleBackColor = true;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(15, 618);
            label6.Name = "label6";
            label6.Size = new Size(74, 19);
            label6.TabIndex = 25;
            label6.Text = "项目地址：";
            // 
            // lblProcess
            // 
            lblProcess.AutoSize = true;
            lblProcess.Location = new Point(971, 12);
            lblProcess.Name = "lblProcess";
            lblProcess.Size = new Size(13, 19);
            lblProcess.TabIndex = 27;
            lblProcess.Text = " ";
            // 
            // cbRemoveInvalidIndex
            // 
            cbRemoveInvalidIndex.AutoSize = true;
            cbRemoveInvalidIndex.Location = new Point(945, 13);
            cbRemoveInvalidIndex.Name = "cbRemoveInvalidIndex";
            cbRemoveInvalidIndex.Size = new Size(106, 23);
            cbRemoveInvalidIndex.TabIndex = 28;
            cbRemoveInvalidIndex.Text = "移除无效索引";
            cbRemoveInvalidIndex.UseVisualStyleBackColor = true;
            // 
            // lblGithub
            // 
            lblGithub.AutoSize = true;
            lblGithub.Location = new Point(86, 618);
            lblGithub.Name = "lblGithub";
            lblGithub.Size = new Size(244, 19);
            lblGithub.TabIndex = 29;
            lblGithub.TabStop = true;
            lblGithub.Text = "https://github.com/ldqk/ImageSearch";
            lblGithub.LinkClicked += lblGithub_LinkClicked;
            // 
            // buttonClipSearch
            // 
            buttonClipSearch.Location = new Point(488, 74);
            buttonClipSearch.Name = "buttonClipSearch";
            buttonClipSearch.Size = new Size(110, 26);
            buttonClipSearch.TabIndex = 30;
            buttonClipSearch.Text = "从剪切板搜索";
            buttonClipSearch.UseVisualStyleBackColor = true;
            buttonClipSearch.Click += buttonClipSearch_Click;
            // 
            // Form1
            // 
            AllowDrop = true;
            AutoScaleDimensions = new SizeF(8F, 19F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1299, 647);
            Controls.Add(buttonClipSearch);
            Controls.Add(lblGithub);
            Controls.Add(cbRemoveInvalidIndex);
            Controls.Add(lblProcess);
            Controls.Add(label6);
            Controls.Add(cbFlip);
            Controls.Add(cbRotate);
            Controls.Add(numLike);
            Controls.Add(lbSpeed);
            Controls.Add(lbSrcInfo);
            Controls.Add(lblDestInfo);
            Controls.Add(lbElpased);
            Controls.Add(label5);
            Controls.Add(lbIndexCount);
            Controls.Add(label4);
            Controls.Add(dgvResult);
            Controls.Add(label3);
            Controls.Add(picDest);
            Controls.Add(picSource);
            Controls.Add(btnIndex);
            Controls.Add(btnSearch);
            Controls.Add(btnPic);
            Controls.Add(txtPic);
            Controls.Add(label2);
            Controls.Add(btnDirectory);
            Controls.Add(txtDirectory);
            Controls.Add(label1);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "本地以图搜图小工具 by 懒得勤快 (评估版本)";
            FormClosing += Form1_FormClosing;
            Load += Form1_Load;
            DragDrop += Form1_DragDrop;
            DragEnter += Form1_DragEnter;
            ((System.ComponentModel.ISupportInitialize)picSource).EndInit();
            ((System.ComponentModel.ISupportInitialize)picDest).EndInit();
            ((System.ComponentModel.ISupportInitialize)dgvResult).EndInit();
            dgvContextMenuStrip.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)numLike).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private TextBox txtDirectory;
        private Button btnDirectory;
        private Button btnPic;
        private TextBox txtPic;
        private Label label2;
        private Button btnSearch;
        private Button btnIndex;
        private PictureBox picSource;
        private PictureBox picDest;
        private Label label3;
        private DataGridView dgvResult;
        private Label label4;
        private Label lbIndexCount;
        private Label label5;
        private Label lbElpased;
        private Label lblDestInfo;
        private Label lbSrcInfo;
        private Label lbSpeed;
        private NumericUpDown numLike;
        private CheckBox cbRotate;
        private CheckBox cbFlip;
        private Label label6;
        private Label lblProcess;
        private CheckBox cbRemoveInvalidIndex;
        private LinkLabel lblGithub;
        private Button buttonClipSearch;
        private ContextMenuStrip dgvContextMenuStrip;
        private ToolStripMenuItem 打开所在文件夹;
        private ToolStripMenuItem 删除;
        private ToolStripMenuItem 删除到回收站ToolStripMenuItem;
    }
}
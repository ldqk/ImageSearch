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
            label1.Location = new Point(12, 9);
            label1.Name = "label1";
            label1.Size = new Size(95, 17);
            label1.TabIndex = 0;
            label1.Text = "添加搜索文件夹:";
            // 
            // txtDirectory
            // 
            txtDirectory.AllowDrop = true;
            txtDirectory.Location = new Point(113, 6);
            txtDirectory.Name = "txtDirectory";
            txtDirectory.Size = new Size(504, 23);
            txtDirectory.TabIndex = 1;
            txtDirectory.DragDrop += txtDirectory_DragDrop;
            txtDirectory.DragEnter += txtDirectory_DragEnter;
            // 
            // btnDirectory
            // 
            btnDirectory.Location = new Point(620, 6);
            btnDirectory.Name = "btnDirectory";
            btnDirectory.Size = new Size(64, 23);
            btnDirectory.TabIndex = 2;
            btnDirectory.Text = "选择";
            btnDirectory.UseVisualStyleBackColor = true;
            btnDirectory.Click += btnDirectory_Click;
            // 
            // btnPic
            // 
            btnPic.Location = new Point(620, 39);
            btnPic.Name = "btnPic";
            btnPic.Size = new Size(64, 23);
            btnPic.TabIndex = 5;
            btnPic.Text = "选择";
            btnPic.UseVisualStyleBackColor = true;
            btnPic.Click += btnPic_Click;
            // 
            // txtPic
            // 
            txtPic.AllowDrop = true;
            txtPic.Location = new Point(113, 39);
            txtPic.Name = "txtPic";
            txtPic.Size = new Size(504, 23);
            txtPic.TabIndex = 0;
            txtPic.DragDrop += txtPic_DragDrop;
            txtPic.DragEnter += txtPic_DragEnter;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(13, 42);
            label2.Name = "label2";
            label2.Size = new Size(95, 17);
            label2.TabIndex = 3;
            label2.Text = "用于检索的图片:";
            // 
            // btnSearch
            // 
            btnSearch.Location = new Point(371, 66);
            btnSearch.Name = "btnSearch";
            btnSearch.Size = new Size(51, 23);
            btnSearch.TabIndex = 6;
            btnSearch.Text = "搜索";
            btnSearch.UseVisualStyleBackColor = true;
            btnSearch.Click += btnSearch_Click;
            // 
            // btnIndex
            // 
            btnIndex.Location = new Point(690, 6);
            btnIndex.Name = "btnIndex";
            btnIndex.Size = new Size(75, 23);
            btnIndex.TabIndex = 7;
            btnIndex.Text = "更新索引";
            btnIndex.UseVisualStyleBackColor = true;
            btnIndex.Click += btnIndex_Click;
            // 
            // picSource
            // 
            picSource.Location = new Point(618, 93);
            picSource.Name = "picSource";
            picSource.Size = new Size(272, 167);
            picSource.SizeMode = PictureBoxSizeMode.Zoom;
            picSource.TabIndex = 9;
            picSource.TabStop = false;
            picSource.LoadCompleted += picSource_LoadCompleted;
            picSource.DoubleClick += picSource_Click;
            // 
            // picDest
            // 
            picDest.Location = new Point(618, 288);
            picDest.Name = "picDest";
            picDest.Size = new Size(272, 189);
            picDest.SizeMode = PictureBoxSizeMode.Zoom;
            picDest.TabIndex = 10;
            picDest.TabStop = false;
            picDest.LoadCompleted += picDest_LoadCompleted;
            picDest.DoubleClick += picDest_Click;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(31, 70);
            label3.Name = "label3";
            label3.Size = new Size(80, 17);
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
            dgvResult.Location = new Point(12, 93);
            dgvResult.Name = "dgvResult";
            dgvResult.ReadOnly = true;
            dgvResult.Size = new Size(605, 386);
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
            dgvContextMenuStrip.Items.AddRange(new ToolStripItem[] { 打开所在文件夹 });
            dgvContextMenuStrip.Name = "dgvContextMenuStrip";
            dgvContextMenuStrip.Size = new Size(161, 26);
            // 
            // 打开所在文件夹
            // 
            打开所在文件夹.Name = "打开所在文件夹";
            打开所在文件夹.Size = new Size(160, 22);
            打开所在文件夹.Text = "打开所在文件夹";
            打开所在文件夹.Click += 打开所在文件夹_Click;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(697, 42);
            label4.Name = "label4";
            label4.Size = new Size(80, 17);
            label4.TabIndex = 14;
            label4.Text = "索引总数量：";
            // 
            // lbIndexCount
            // 
            lbIndexCount.AutoSize = true;
            lbIndexCount.Location = new Point(782, 42);
            lbIndexCount.Name = "lbIndexCount";
            lbIndexCount.Size = new Size(0, 17);
            lbIndexCount.TabIndex = 15;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(529, 70);
            label5.Name = "label5";
            label5.Size = new Size(68, 17);
            label5.TabIndex = 16;
            label5.Text = "搜索耗时：";
            // 
            // lbElpased
            // 
            lbElpased.AutoSize = true;
            lbElpased.Location = new Point(603, 69);
            lbElpased.Name = "lbElpased";
            lbElpased.Size = new Size(0, 17);
            lbElpased.TabIndex = 17;
            // 
            // lblDestInfo
            // 
            lblDestInfo.AutoSize = true;
            lblDestInfo.Location = new Point(627, 460);
            lblDestInfo.Name = "lblDestInfo";
            lblDestInfo.Size = new Size(0, 17);
            lblDestInfo.TabIndex = 18;
            // 
            // lbSrcInfo
            // 
            lbSrcInfo.AutoSize = true;
            lbSrcInfo.Location = new Point(626, 243);
            lbSrcInfo.Name = "lbSrcInfo";
            lbSrcInfo.Size = new Size(0, 17);
            lbSrcInfo.TabIndex = 19;
            // 
            // lbSpeed
            // 
            lbSpeed.AutoSize = true;
            lbSpeed.Location = new Point(697, 67);
            lbSpeed.Name = "lbSpeed";
            lbSpeed.Size = new Size(16, 17);
            lbSpeed.TabIndex = 20;
            lbSpeed.Text = "  ";
            // 
            // numLike
            // 
            numLike.Location = new Point(114, 67);
            numLike.Minimum = new decimal(new int[] { 70, 0, 0, 0 });
            numLike.Name = "numLike";
            numLike.Size = new Size(45, 23);
            numLike.TabIndex = 21;
            numLike.Value = new decimal(new int[] { 90, 0, 0, 0 });
            // 
            // cbRotate
            // 
            cbRotate.AutoSize = true;
            cbRotate.Location = new Point(171, 69);
            cbRotate.Name = "cbRotate";
            cbRotate.Size = new Size(87, 21);
            cbRotate.TabIndex = 22;
            cbRotate.Text = "查找已旋转";
            cbRotate.UseVisualStyleBackColor = true;
            // 
            // cbFlip
            // 
            cbFlip.AutoSize = true;
            cbFlip.Location = new Point(275, 69);
            cbFlip.Name = "cbFlip";
            cbFlip.Size = new Size(87, 21);
            cbFlip.TabIndex = 23;
            cbFlip.Text = "查找已翻转";
            cbFlip.UseVisualStyleBackColor = true;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(31, 484);
            label6.Name = "label6";
            label6.Size = new Size(68, 17);
            label6.TabIndex = 25;
            label6.Text = "项目地址：";
            // 
            // lblProcess
            // 
            lblProcess.AutoSize = true;
            lblProcess.Location = new Point(793, 8);
            lblProcess.Name = "lblProcess";
            lblProcess.Size = new Size(12, 17);
            lblProcess.TabIndex = 27;
            lblProcess.Text = " ";
            // 
            // cbRemoveInvalidIndex
            // 
            cbRemoveInvalidIndex.AutoSize = true;
            cbRemoveInvalidIndex.Location = new Point(770, 9);
            cbRemoveInvalidIndex.Name = "cbRemoveInvalidIndex";
            cbRemoveInvalidIndex.Size = new Size(99, 21);
            cbRemoveInvalidIndex.TabIndex = 28;
            cbRemoveInvalidIndex.Text = "移除无效索引";
            cbRemoveInvalidIndex.UseVisualStyleBackColor = true;
            // 
            // lblGithub
            // 
            lblGithub.AutoSize = true;
            lblGithub.Location = new Point(93, 484);
            lblGithub.Name = "lblGithub";
            lblGithub.Size = new Size(227, 17);
            lblGithub.TabIndex = 29;
            lblGithub.TabStop = true;
            lblGithub.Text = "https://github.com/ldqk/ImageSearch";
            lblGithub.LinkClicked += lblGithub_LinkClicked;
            // 
            // buttonClipSearch
            // 
            buttonClipSearch.Location = new Point(427, 66);
            buttonClipSearch.Name = "buttonClipSearch";
            buttonClipSearch.Size = new Size(96, 23);
            buttonClipSearch.TabIndex = 30;
            buttonClipSearch.Text = "从剪切板搜索";
            buttonClipSearch.UseVisualStyleBackColor = true;
            buttonClipSearch.Click += buttonClipSearch_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(902, 506);
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
    }
}
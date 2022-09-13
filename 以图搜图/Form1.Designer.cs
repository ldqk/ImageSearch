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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.label1 = new System.Windows.Forms.Label();
            this.txtDirectory = new System.Windows.Forms.TextBox();
            this.btnDirectory = new System.Windows.Forms.Button();
            this.btnPic = new System.Windows.Forms.Button();
            this.txtPic = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.btnSearch = new System.Windows.Forms.Button();
            this.btnIndex = new System.Windows.Forms.Button();
            this.picSource = new System.Windows.Forms.PictureBox();
            this.picDest = new System.Windows.Forms.PictureBox();
            this.label3 = new System.Windows.Forms.Label();
            this.dgvResult = new System.Windows.Forms.DataGridView();
            this.label4 = new System.Windows.Forms.Label();
            this.lbIndexCount = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.lbElpased = new System.Windows.Forms.Label();
            this.lblDestInfo = new System.Windows.Forms.Label();
            this.lbSrcInfo = new System.Windows.Forms.Label();
            this.lbSpeed = new System.Windows.Forms.Label();
            this.numLike = new System.Windows.Forms.NumericUpDown();
            this.cbRotate = new System.Windows.Forms.CheckBox();
            this.cbFlip = new System.Windows.Forms.CheckBox();
            this.label6 = new System.Windows.Forms.Label();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.lblProcess = new System.Windows.Forms.Label();
            this.cbRemoveInvalidIndex = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.picSource)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picDest)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvResult)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numLike)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(95, 17);
            this.label1.TabIndex = 0;
            this.label1.Text = "添加搜索文件夹:";
            // 
            // txtDirectory
            // 
            this.txtDirectory.Location = new System.Drawing.Point(113, 6);
            this.txtDirectory.Name = "txtDirectory";
            this.txtDirectory.Size = new System.Drawing.Size(504, 23);
            this.txtDirectory.TabIndex = 1;
            // 
            // btnDirectory
            // 
            this.btnDirectory.Location = new System.Drawing.Point(633, 6);
            this.btnDirectory.Name = "btnDirectory";
            this.btnDirectory.Size = new System.Drawing.Size(75, 23);
            this.btnDirectory.TabIndex = 2;
            this.btnDirectory.Text = "选择";
            this.btnDirectory.UseVisualStyleBackColor = true;
            this.btnDirectory.Click += new System.EventHandler(this.btnDirectory_Click);
            // 
            // btnPic
            // 
            this.btnPic.Location = new System.Drawing.Point(633, 39);
            this.btnPic.Name = "btnPic";
            this.btnPic.Size = new System.Drawing.Size(75, 23);
            this.btnPic.TabIndex = 5;
            this.btnPic.Text = "选择";
            this.btnPic.UseVisualStyleBackColor = true;
            this.btnPic.Click += new System.EventHandler(this.btnPic_Click);
            // 
            // txtPic
            // 
            this.txtPic.Location = new System.Drawing.Point(113, 39);
            this.txtPic.Name = "txtPic";
            this.txtPic.Size = new System.Drawing.Size(504, 23);
            this.txtPic.TabIndex = 4;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(13, 42);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(95, 17);
            this.label2.TabIndex = 3;
            this.label2.Text = "用于检索的图片:";
            // 
            // btnSearch
            // 
            this.btnSearch.Location = new System.Drawing.Point(408, 66);
            this.btnSearch.Name = "btnSearch";
            this.btnSearch.Size = new System.Drawing.Size(75, 23);
            this.btnSearch.TabIndex = 6;
            this.btnSearch.Text = "搜索";
            this.btnSearch.UseVisualStyleBackColor = true;
            this.btnSearch.Click += new System.EventHandler(this.btnSearch_Click);
            // 
            // btnIndex
            // 
            this.btnIndex.Location = new System.Drawing.Point(712, 6);
            this.btnIndex.Name = "btnIndex";
            this.btnIndex.Size = new System.Drawing.Size(75, 23);
            this.btnIndex.TabIndex = 7;
            this.btnIndex.Text = "更新索引";
            this.btnIndex.UseVisualStyleBackColor = true;
            this.btnIndex.Click += new System.EventHandler(this.btnIndex_Click);
            // 
            // picSource
            // 
            this.picSource.Location = new System.Drawing.Point(618, 93);
            this.picSource.Name = "picSource";
            this.picSource.Size = new System.Drawing.Size(272, 167);
            this.picSource.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picSource.TabIndex = 9;
            this.picSource.TabStop = false;
            this.picSource.LoadCompleted += new System.ComponentModel.AsyncCompletedEventHandler(this.picSource_LoadCompleted);
            // 
            // picDest
            // 
            this.picDest.Location = new System.Drawing.Point(618, 288);
            this.picDest.Name = "picDest";
            this.picDest.Size = new System.Drawing.Size(272, 189);
            this.picDest.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picDest.TabIndex = 10;
            this.picDest.TabStop = false;
            this.picDest.LoadCompleted += new System.ComponentModel.AsyncCompletedEventHandler(this.picDest_LoadCompleted);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(31, 70);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(80, 17);
            this.label3.TabIndex = 11;
            this.label3.Text = "查找相似度：";
            // 
            // dgvResult
            // 
            this.dgvResult.AllowUserToAddRows = false;
            this.dgvResult.AllowUserToDeleteRows = false;
            this.dgvResult.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.dgvResult.BackgroundColor = System.Drawing.SystemColors.Control;
            this.dgvResult.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvResult.Location = new System.Drawing.Point(12, 93);
            this.dgvResult.Name = "dgvResult";
            this.dgvResult.ReadOnly = true;
            this.dgvResult.RowTemplate.Height = 25;
            this.dgvResult.Size = new System.Drawing.Size(605, 386);
            this.dgvResult.TabIndex = 13;
            this.dgvResult.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvResult_CellClick);
            this.dgvResult.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvResult_CellContentClick);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(717, 42);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(80, 17);
            this.label4.TabIndex = 14;
            this.label4.Text = "索引总数量：";
            // 
            // lbIndexCount
            // 
            this.lbIndexCount.AutoSize = true;
            this.lbIndexCount.Location = new System.Drawing.Point(789, 42);
            this.lbIndexCount.Name = "lbIndexCount";
            this.lbIndexCount.Size = new System.Drawing.Size(0, 17);
            this.lbIndexCount.TabIndex = 15;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(487, 70);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(68, 17);
            this.label5.TabIndex = 16;
            this.label5.Text = "搜索耗时：";
            // 
            // lbElpased
            // 
            this.lbElpased.AutoSize = true;
            this.lbElpased.Location = new System.Drawing.Point(561, 69);
            this.lbElpased.Name = "lbElpased";
            this.lbElpased.Size = new System.Drawing.Size(0, 17);
            this.lbElpased.TabIndex = 17;
            // 
            // lblDestInfo
            // 
            this.lblDestInfo.AutoSize = true;
            this.lblDestInfo.Location = new System.Drawing.Point(627, 460);
            this.lblDestInfo.Name = "lblDestInfo";
            this.lblDestInfo.Size = new System.Drawing.Size(0, 17);
            this.lblDestInfo.TabIndex = 18;
            // 
            // lbSrcInfo
            // 
            this.lbSrcInfo.AutoSize = true;
            this.lbSrcInfo.Location = new System.Drawing.Point(626, 243);
            this.lbSrcInfo.Name = "lbSrcInfo";
            this.lbSrcInfo.Size = new System.Drawing.Size(0, 17);
            this.lbSrcInfo.TabIndex = 19;
            // 
            // lbSpeed
            // 
            this.lbSpeed.AutoSize = true;
            this.lbSpeed.Location = new System.Drawing.Point(717, 67);
            this.lbSpeed.Name = "lbSpeed";
            this.lbSpeed.Size = new System.Drawing.Size(16, 17);
            this.lbSpeed.TabIndex = 20;
            this.lbSpeed.Text = "  ";
            // 
            // numLike
            // 
            this.numLike.Location = new System.Drawing.Point(114, 67);
            this.numLike.Minimum = new decimal(new int[] {
            70,
            0,
            0,
            0});
            this.numLike.Name = "numLike";
            this.numLike.Size = new System.Drawing.Size(45, 23);
            this.numLike.TabIndex = 21;
            this.numLike.Value = new decimal(new int[] {
            90,
            0,
            0,
            0});
            // 
            // cbRotate
            // 
            this.cbRotate.AutoSize = true;
            this.cbRotate.Location = new System.Drawing.Point(171, 69);
            this.cbRotate.Name = "cbRotate";
            this.cbRotate.Size = new System.Drawing.Size(87, 21);
            this.cbRotate.TabIndex = 22;
            this.cbRotate.Text = "查找已旋转";
            this.cbRotate.UseVisualStyleBackColor = true;
            // 
            // cbFlip
            // 
            this.cbFlip.AutoSize = true;
            this.cbFlip.Location = new System.Drawing.Point(278, 69);
            this.cbFlip.Name = "cbFlip";
            this.cbFlip.Size = new System.Drawing.Size(87, 21);
            this.cbFlip.TabIndex = 23;
            this.cbFlip.Text = "查找已翻转";
            this.cbFlip.UseVisualStyleBackColor = true;
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(31, 484);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(68, 17);
            this.label6.TabIndex = 25;
            this.label6.Text = "项目地址：";
            // 
            // textBox1
            // 
            this.textBox1.Location = new System.Drawing.Point(105, 481);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(290, 23);
            this.textBox1.TabIndex = 26;
            this.textBox1.Text = "https://github.com/ldqk/ImageSearch";
            // 
            // lblProcess
            // 
            this.lblProcess.AutoSize = true;
            this.lblProcess.Location = new System.Drawing.Point(793, 8);
            this.lblProcess.Name = "lblProcess";
            this.lblProcess.Size = new System.Drawing.Size(12, 17);
            this.lblProcess.TabIndex = 27;
            this.lblProcess.Text = " ";
            // 
            // cbRemoveInvalidIndex
            // 
            this.cbRemoveInvalidIndex.AutoSize = true;
            this.cbRemoveInvalidIndex.Location = new System.Drawing.Point(793, 9);
            this.cbRemoveInvalidIndex.Name = "cbRemoveInvalidIndex";
            this.cbRemoveInvalidIndex.Size = new System.Drawing.Size(99, 21);
            this.cbRemoveInvalidIndex.TabIndex = 28;
            this.cbRemoveInvalidIndex.Text = "移除无效索引";
            this.cbRemoveInvalidIndex.UseVisualStyleBackColor = true;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(902, 506);
            this.Controls.Add(this.cbRemoveInvalidIndex);
            this.Controls.Add(this.lblProcess);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.cbFlip);
            this.Controls.Add(this.cbRotate);
            this.Controls.Add(this.numLike);
            this.Controls.Add(this.lbSpeed);
            this.Controls.Add(this.lbSrcInfo);
            this.Controls.Add(this.lblDestInfo);
            this.Controls.Add(this.lbElpased);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.lbIndexCount);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.dgvResult);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.picDest);
            this.Controls.Add(this.picSource);
            this.Controls.Add(this.btnIndex);
            this.Controls.Add(this.btnSearch);
            this.Controls.Add(this.btnPic);
            this.Controls.Add(this.txtPic);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.btnDirectory);
            this.Controls.Add(this.txtDirectory);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "本地以图搜图小工具 by 懒得勤快";
            this.Load += new System.EventHandler(this.Form1_Load);
            ((System.ComponentModel.ISupportInitialize)(this.picSource)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picDest)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvResult)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numLike)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

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
        private TextBox textBox1;
        private Label lblProcess;
        private CheckBox cbRemoveInvalidIndex;
    }
}
namespace 以图搜图
{
    partial class ErrorsDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            textBox = new TextBox();
            SuspendLayout();
            // 
            // textBox
            // 
            textBox.Location = new Point(13, 7);
            textBox.Multiline = true;
            textBox.Name = "textBox";
            textBox.ScrollBars = ScrollBars.Vertical;
            textBox.Size = new Size(775, 431);
            textBox.TabIndex = 0;
            // 
            // ErrorsDialog
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(textBox);
            Name = "ErrorsDialog";
            StartPosition = FormStartPosition.CenterParent;
            Text = "错误日志";
            Load += ErrorsDialog_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox textBox;
    }
}
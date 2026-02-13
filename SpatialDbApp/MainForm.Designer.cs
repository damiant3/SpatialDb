namespace SpatialDbApp
{
    public partial class MainForm : Form
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
            rtbLog = new RichTextBox();
            btnRun = new Button();
            nudObjCount = new NumericUpDown();
            nudTime = new NumericUpDown();
            label1 = new Label();
            label2 = new Label();
            cmbLoadFile = new ComboBox();
            label3 = new Label();
            lblDisplayCount = new Label();
            nudDisplayCount = new NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)nudObjCount).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudTime).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudDisplayCount).BeginInit();
            SuspendLayout();
            // 
            // rtbLog
            // 
            rtbLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            rtbLog.BackColor = SystemColors.ActiveCaptionText;
            rtbLog.ForeColor = Color.Lime;
            rtbLog.Location = new Point(0, 39);
            rtbLog.Name = "rtbLog";
            rtbLog.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtbLog.Size = new Size(218, 406);
            rtbLog.TabIndex = 0;
            rtbLog.Text = "";
            // 
            // btnRun
            // 
            btnRun.Location = new Point(9, 7);
            btnRun.Name = "btnRun";
            btnRun.Size = new Size(121, 23);
            btnRun.TabIndex = 1;
            btnRun.Text = "Run Grand Sim";
            btnRun.UseVisualStyleBackColor = true;
            btnRun.Click += btnRun_Click;
            // 
            // nudObjCount
            // 
            nudObjCount.Location = new Point(193, 7);
            nudObjCount.Maximum = new decimal(new int[] { 100000000, 0, 0, 0 });
            nudObjCount.Name = "nudObjCount";
            nudObjCount.Size = new Size(120, 23);
            nudObjCount.TabIndex = 2;
            nudObjCount.Value = new decimal(new int[] { 100000, 0, 0, 0 });
            nudObjCount.ValueChanged += nudObjCount_ValueChanged;
            // 
            // nudTime
            // 
            nudTime.Location = new Point(557, 7);
            nudTime.Maximum = new decimal(new int[] { 100000000, 0, 0, 0 });
            nudTime.Name = "nudTime";
            nudTime.Size = new Size(55, 23);
            nudTime.TabIndex = 3;
            nudTime.Value = new decimal(new int[] { 5, 0, 0, 0 });
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(138, 11);
            label1.Name = "label1";
            label1.Size = new Size(47, 15);
            label1.TabIndex = 4;
            label1.Text = "Objects";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(498, 11);
            label2.Name = "label2";
            label2.Size = new Size(51, 15);
            label2.TabIndex = 5;
            label2.Text = "Seconds";
            // 
            // cmbLoadFile
            // 
            cmbLoadFile.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbLoadFile.FormattingEnabled = true;
            cmbLoadFile.Location = new Point(664, 7);
            cmbLoadFile.Name = "cmbLoadFile";
            cmbLoadFile.Size = new Size(124, 23);
            cmbLoadFile.TabIndex = 6;
            cmbLoadFile.SelectedIndexChanged += cmbLoadFile_SelectedIndexChanged;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(620, 11);
            label3.Name = "label3";
            label3.Size = new Size(36, 15);
            label3.TabIndex = 7;
            label3.Text = "Load:";
            // 
            // lblDisplayCount
            // 
            lblDisplayCount.AutoSize = true;
            lblDisplayCount.Location = new Point(321, 11);
            lblDisplayCount.Name = "lblDisplayCount";
            lblDisplayCount.Size = new Size(81, 15);
            lblDisplayCount.TabIndex = 8;
            lblDisplayCount.Text = "Render count:";
            // 
            // nudDisplayCount
            // 
            nudDisplayCount.Location = new Point(410, 7);
            nudDisplayCount.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            nudDisplayCount.Name = "nudDisplayCount";
            nudDisplayCount.Size = new Size(80, 23);
            nudDisplayCount.TabIndex = 9;
            nudDisplayCount.Value = new decimal(new int[] { 5000, 0, 0, 0 });
            nudDisplayCount.ValueChanged += nudDisplayCount_ValueChanged;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(label3);
            Controls.Add(cmbLoadFile);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(nudTime);
            Controls.Add(nudObjCount);
            Controls.Add(btnRun);
            Controls.Add(rtbLog);
            Controls.Add(lblDisplayCount);
            Controls.Add(nudDisplayCount);
            Name = "MainForm";
            Text = "Grand Simulation";
            ((System.ComponentModel.ISupportInitialize)nudObjCount).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudTime).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudDisplayCount).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private RichTextBox rtbLog;
        private Button btnRun;
        private NumericUpDown nudObjCount;
        private NumericUpDown nudTime;
        private Label label1;
        private Label label2;
        private ComboBox cmbLoadFile;
        private Label label3;
        private Label lblDisplayCount;
        private NumericUpDown nudDisplayCount;
    }
}
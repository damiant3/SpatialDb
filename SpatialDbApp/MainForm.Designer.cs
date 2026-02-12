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
            button1 = new Button();
            nudObjCount = new NumericUpDown();
            nudTime = new NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)nudObjCount).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudTime).BeginInit();
            SuspendLayout();
            // 
            // rtbLog
            // 
            rtbLog.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            rtbLog.Location = new Point(0, 342);
            rtbLog.Name = "rtbLog";
            rtbLog.Size = new Size(800, 107);
            rtbLog.TabIndex = 0;
            rtbLog.Text = "";
            // 
            // button1
            // 
            button1.Location = new Point(12, 12);
            button1.Name = "button1";
            button1.Size = new Size(121, 23);
            button1.TabIndex = 1;
            button1.Text = "Run Grand Sim";
            button1.UseVisualStyleBackColor = true;
            // 
            // nudObjCount
            // 
            nudObjCount.Location = new Point(165, 14);
            nudObjCount.Name = "nudObjCount";
            nudObjCount.Size = new Size(120, 23);
            nudObjCount.TabIndex = 2;
            // 
            // nudTime
            // 
            nudTime.Location = new Point(334, 14);
            nudTime.Name = "nudTime";
            nudTime.Size = new Size(120, 23);
            nudTime.TabIndex = 3;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(nudTime);
            Controls.Add(nudObjCount);
            Controls.Add(button1);
            Controls.Add(rtbLog);
            Name = "MainForm";
            Text = "Grand Simulation";
            ((System.ComponentModel.ISupportInitialize)nudObjCount).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudTime).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private RichTextBox rtbLog;
        private Button button1;
        private NumericUpDown nudObjCount;
        private NumericUpDown nudTime;
    }
}
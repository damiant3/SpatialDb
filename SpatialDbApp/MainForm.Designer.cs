namespace SpatialDbApp
{
    partial class MainForm
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
            lblObjects = new Label();
            nudObjectCount = new NumericUpDown();
            lblDuration = new Label();
            nudDuration = new NumericUpDown();
            rtbTypeSummary = new RichTextBox();
            btnStartSimulation = new Button();
            ((System.ComponentModel.ISupportInitialize)nudObjectCount).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudDuration).BeginInit();
            SuspendLayout();
            // 
            // lblObjects
            // 
            lblObjects.AutoSize = true;
            lblObjects.Location = new Point(170, 19);
            lblObjects.Name = "lblObjects";
            lblObjects.Size = new Size(50, 15);
            lblObjects.TabIndex = 2;
            lblObjects.Text = "Objects:";
            // 
            // nudObjectCount
            // 
            nudObjectCount.Location = new Point(226, 17);
            nudObjectCount.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
            nudObjectCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudObjectCount.Name = "nudObjectCount";
            nudObjectCount.Size = new Size(80, 23);
            nudObjectCount.TabIndex = 3;
            nudObjectCount.Value = new decimal(new int[] { 100000, 0, 0, 0 });
            // 
            // lblDuration
            // 
            lblDuration.AutoSize = true;
            lblDuration.Location = new Point(312, 19);
            lblDuration.Name = "lblDuration";
            lblDuration.Size = new Size(84, 15);
            lblDuration.TabIndex = 4;
            lblDuration.Text = "Duration (sec):";
            // 
            // nudDuration
            // 
            nudDuration.Location = new Point(401, 17);
            nudDuration.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
            nudDuration.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudDuration.Name = "nudDuration";
            nudDuration.Size = new Size(80, 23);
            nudDuration.TabIndex = 5;
            nudDuration.Value = new decimal(new int[] { 60, 0, 0, 0 });
            // 
            // rtbTypeSummary
            // 
            rtbTypeSummary.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            rtbTypeSummary.Location = new Point(12, 51);
            rtbTypeSummary.Name = "rtbTypeSummary";
            rtbTypeSummary.Size = new Size(776, 376);
            rtbTypeSummary.TabIndex = 0;
            rtbTypeSummary.Text = "";
            // 
            // btnStartSimulation
            // 
            btnStartSimulation.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnStartSimulation.Location = new Point(12, 11);
            btnStartSimulation.Name = "btnStartSimulation";
            btnStartSimulation.Size = new Size(150, 30);
            btnStartSimulation.TabIndex = 1;
            btnStartSimulation.Text = "Start Grand Simulation";
            btnStartSimulation.UseVisualStyleBackColor = true;
            btnStartSimulation.Click += BtnStartSimulation_Click;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 439);
            Controls.Add(nudDuration);
            Controls.Add(lblDuration);
            Controls.Add(nudObjectCount);
            Controls.Add(lblObjects);
            Controls.Add(btnStartSimulation);
            Controls.Add(rtbTypeSummary);
            Name = "MainForm";
            Text = "Grand Simulator";
            ((System.ComponentModel.ISupportInitialize)nudObjectCount).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudDuration).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private RichTextBox rtbTypeSummary;
        private Button btnStartSimulation;
        private Label lblObjects;
        private NumericUpDown nudObjectCount;
        private Label lblDuration;
        private NumericUpDown nudDuration;
    }
}

namespace ESIcontrol
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Button btnStartSequence;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.TextBox txtLog;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.btnStartSequence = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.txtCurrentCondition = new System.Windows.Forms.TextBox();
            this.lblPhase = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // btnStartSequence
            // 
            this.btnStartSequence.Location = new System.Drawing.Point(12, 12);
            this.btnStartSequence.Name = "btnStartSequence";
            this.btnStartSequence.Size = new System.Drawing.Size(140, 40);
            this.btnStartSequence.TabIndex = 0;
            this.btnStartSequence.Text = "Start Sequence";
            this.btnStartSequence.UseVisualStyleBackColor = true;
            this.btnStartSequence.Click += new System.EventHandler(this.btnStartSequence_Click);
            // 
            // btnStop
            // 
            this.btnStop.Location = new System.Drawing.Point(170, 12);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(140, 40);
            this.btnStop.TabIndex = 1;
            this.btnStop.Text = "Stop";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            // 
            // txtLog
            // 
            this.txtLog.Location = new System.Drawing.Point(12, 70);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(675, 360);
            this.txtLog.TabIndex = 2;
            // 
            // txtCurrentCondition
            //
            this.txtCurrentCondition.Location = new System.Drawing.Point(704, 70);
            this.txtCurrentCondition.Multiline = true;
            this.txtCurrentCondition.Name = "txtCurrentCondition";
            this.txtCurrentCondition.ReadOnly = true;
            this.txtCurrentCondition.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtCurrentCondition.Size = new System.Drawing.Size(325, 360);
            this.txtCurrentCondition.TabIndex = 3;
            //
            // lblPhase
            //
            this.lblPhase.AutoSize = true;
            this.lblPhase.Location = new System.Drawing.Point(702, 40);
            this.lblPhase.Name = "lblPhase";
            this.lblPhase.Size = new System.Drawing.Size(35, 12);
            this.lblPhase.TabIndex = 4;
            this.lblPhase.Text = "label1";
            //
            // MainForm
            // 
            this.ClientSize = new System.Drawing.Size(1050, 450);
            this.Controls.Add(this.lblPhase);
            this.Controls.Add(this.txtCurrentCondition);
            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.btnStartSequence);
            this.Name = "MainForm";
            this.Text = "ESI Simple GUI";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.TextBox txtCurrentCondition;
        private System.Windows.Forms.Label lblPhase;
    }
}

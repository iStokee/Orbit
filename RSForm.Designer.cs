namespace Orbit
{
    partial class RSForm
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
			this.panel_DockPanel = new System.Windows.Forms.Panel();
			this.SuspendLayout();
			// 
			// panel_DockPanel
			// 
			this.panel_DockPanel.AutoSize = true;
			this.panel_DockPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(24)))), ((int)(((byte)(24)))), ((int)(((byte)(24)))));
			this.panel_DockPanel.Cursor = System.Windows.Forms.Cursors.Arrow;
			this.panel_DockPanel.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panel_DockPanel.Location = new System.Drawing.Point(0, 0);
			this.panel_DockPanel.Name = "panel_DockPanel";
			this.panel_DockPanel.Size = new System.Drawing.Size(686, 378);
			this.panel_DockPanel.TabIndex = 0;
			// 
			// RSForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSize = true;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.ClientSize = new System.Drawing.Size(686, 378);
			this.Controls.Add(this.panel_DockPanel);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
			this.Name = "RSForm";
			this.Text = "RSForm";
			this.Load += new System.EventHandler(this.RSForm_Load);
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Panel panel_DockPanel;
    }
}
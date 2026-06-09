using System.Drawing;
using System.Windows.Forms;

namespace MD5Viewer
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            // ── colours ──────────────────────────────────────────────────
            Color bgDark      = Color.FromArgb(14,  14,  20);
            Color barDark     = Color.FromArgb(20,  20,  28);
            Color ctrlDark    = Color.FromArgb(34,  35,  48);
            Color ctrlBorder  = Color.FromArgb(55,  56,  72);
            Color textPrimary = Color.FromArgb(220, 220, 230);
            Color textMuted   = Color.FromArgb(120, 122, 140);
            var   uiFont      = new Font("Segoe UI", 9f);
            var   iconFont    = new Font("Segoe UI Symbol", 13f);

            // ── controls ─────────────────────────────────────────────────
            viewport1       = new Viewport();
            bottomBar       = new Panel();
            leftGroup       = new FlowLayoutPanel();
            btnReset        = new Button();
            btnPlay         = new Button();
            animationCombo  = new ComboBox();
            centerGroup     = new Panel();
            timelineSlider  = new TrackBar();
            lblFrameInfo    = new Label();
            rightGroup      = new FlowLayoutPanel();
            renderModeCombo = new ComboBox();
            titleLabel      = new Label();

            SuspendLayout();
            bottomBar.SuspendLayout();
            leftGroup.SuspendLayout();
            centerGroup.SuspendLayout();
            rightGroup.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)timelineSlider).BeginInit();

            // ── viewport (fills everything behind the bars) ───────────────
            viewport1.BackColor = bgDark;
            viewport1.Dock      = DockStyle.Fill;
            viewport1.Name      = "viewport1";
            viewport1.Phase     = RenderPhase.FullRender;
            viewport1.TabIndex  = 0;

            // ── title label (top-left overlay, drawn over the viewport) ───
            titleLabel.AutoSize  = true;
            titleLabel.BackColor = Color.FromArgb(120, 14, 14, 20);
            titleLabel.ForeColor = textPrimary;
            titleLabel.Font      = new Font("Segoe UI", 10.5f, FontStyle.Regular);
            titleLabel.Padding   = new Padding(10, 6, 10, 6);
            titleLabel.Location  = new Point(12, 10);
            titleLabel.Name      = "titleLabel";
            titleLabel.Text      = "Cyberdemon  -  Doom 3";

            // ── bottom bar ────────────────────────────────────────────────
            bottomBar.BackColor = barDark;
            bottomBar.Dock      = DockStyle.Bottom;
            bottomBar.Height    = 56;
            bottomBar.Padding   = new Padding(6, 8, 6, 8);
            bottomBar.Name      = "bottomBar";

            // LEFT: reset + play + anim combo
            leftGroup.AutoSize       = false;
            leftGroup.Dock           = DockStyle.Left;
            leftGroup.Width          = 310;
            leftGroup.BackColor      = Color.Transparent;
            leftGroup.FlowDirection  = FlowDirection.LeftToRight;
            leftGroup.WrapContents   = false;
            leftGroup.Padding        = new Padding(0, 0, 0, 0);
            leftGroup.Name           = "leftGroup";

            void StyleIconBtn(Button b, string icon)
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize          = 0;
                b.FlatAppearance.MouseOverBackColor  = Color.FromArgb(50, 52, 68);
                b.FlatAppearance.MouseDownBackColor  = Color.FromArgb(40, 42, 58);
                b.BackColor  = Color.Transparent;
                b.ForeColor  = textPrimary;
                b.Font       = iconFont;
                b.Text       = icon;
                b.Size       = new Size(38, 38);
                b.Margin     = new Padding(0, 0, 2, 0);
                b.TabStop    = false;
                b.Cursor     = Cursors.Hand;
            }

            StyleIconBtn(btnReset, "\u23EE");   // ⏮
            btnReset.Name    = "btnReset";
            btnReset.Click  += btnReset_Click;

            StyleIconBtn(btnPlay, "\u23F8");    // ⏸
            btnPlay.Name    = "btnPlay";
            btnPlay.Click  += btnPlay_Click;

            animationCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            animationCombo.BackColor     = ctrlDark;
            animationCombo.ForeColor     = textPrimary;
            animationCombo.FlatStyle     = FlatStyle.Flat;
            animationCombo.Font          = uiFont;
            animationCombo.Size          = new Size(192, 26);
            animationCombo.Margin        = new Padding(6, 6, 0, 0);
            animationCombo.Name          = "animationCombo";
            animationCombo.SelectedIndexChanged += animationCombo_SelectedIndexChanged;

            leftGroup.Controls.Add(btnReset);
            leftGroup.Controls.Add(btnPlay);
            leftGroup.Controls.Add(animationCombo);

            // RIGHT: render mode combo
            rightGroup.AutoSize      = false;
            rightGroup.Dock          = DockStyle.Right;
            // Increase width so combobox + diagnostic toggles fit comfortably
            rightGroup.Width         = 360;
            rightGroup.BackColor     = Color.Transparent;
            // LeftToRight makes controls lay out in natural order: combo then checkboxes
            rightGroup.FlowDirection = FlowDirection.LeftToRight;
            rightGroup.WrapContents  = false;
            rightGroup.Padding       = new Padding(6, 6, 6, 6);
            rightGroup.Name          = "rightGroup";

            renderModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            renderModeCombo.BackColor     = ctrlDark;
            renderModeCombo.ForeColor     = textPrimary;
            renderModeCombo.FlatStyle     = FlatStyle.Flat;
            renderModeCombo.Font          = uiFont;
            // Make combobox wider and give right margin so it doesn't crowd the diag toggles
            renderModeCombo.Size          = new Size(220, 26);
            renderModeCombo.Margin        = new Padding(0, 6, 8, 0);
            renderModeCombo.Name          = "renderModeCombo";
            renderModeCombo.SelectedIndexChanged += renderModeCombo_SelectedIndexChanged;

            // Add small diagnostic toggles next to render mode
            var diagPanel = new FlowLayoutPanel();
            diagPanel.AutoSize = true;
            diagPanel.FlowDirection = FlowDirection.LeftToRight;
            diagPanel.BackColor = Color.Transparent;
            diagPanel.Margin = new Padding(6, 6, 6, 0);
            diagPanel.Padding = new Padding(4, 4, 4, 4);
            diagPanel.BorderStyle = BorderStyle.None;

            var chkNormalFlip = new CheckBox();
            chkNormalFlip.Text = "N flip";
            chkNormalFlip.Checked = false;
            chkNormalFlip.AutoSize = true;
            chkNormalFlip.ForeColor = textPrimary;
            chkNormalFlip.Font = new Font("Segoe UI", 8.5f);
            chkNormalFlip.Padding = new Padding(6, 4, 6, 4);
            chkNormalFlip.CheckedChanged += (s, e) => { viewport1.NormalYFlip = chkNormalFlip.Checked; };

            var chkFlipWinding = new CheckBox();
            chkFlipWinding.Text = "FlipW";
            chkFlipWinding.AutoSize = true;
            chkFlipWinding.ForeColor = textPrimary;
            chkFlipWinding.Font = new Font("Segoe UI", 8.5f);
            chkFlipWinding.Padding = new Padding(6, 4, 6, 4);
            chkFlipWinding.CheckedChanged += (s, e) => { viewport1.FlipWinding = chkFlipWinding.Checked; };

            diagPanel.Controls.Add(chkNormalFlip);
            diagPanel.Controls.Add(chkFlipWinding);

            rightGroup.Controls.Add(diagPanel);

            rightGroup.Controls.Add(renderModeCombo);

            // CENTER: timeline + frame label
            centerGroup.Dock      = DockStyle.Fill;
            centerGroup.BackColor = Color.Transparent;
            centerGroup.Name      = "centerGroup";

            timelineSlider.Dock          = DockStyle.Fill;
            timelineSlider.BackColor     = barDark;
            timelineSlider.TickStyle     = TickStyle.None;
            timelineSlider.Minimum       = 0;
            timelineSlider.Maximum       = 100;
            timelineSlider.Value         = 0;
            timelineSlider.Name          = "timelineSlider";
            timelineSlider.Scroll       += timelineSlider_Scroll;

            lblFrameInfo.Dock      = DockStyle.Right;
            lblFrameInfo.AutoSize  = false;
            lblFrameInfo.Width     = 64;
            lblFrameInfo.TextAlign = ContentAlignment.MiddleLeft;
            lblFrameInfo.ForeColor = textMuted;
            lblFrameInfo.BackColor = Color.Transparent;
            lblFrameInfo.Font      = new Font("Segoe UI", 8.5f);
            lblFrameInfo.Text      = "0 / 0";
            lblFrameInfo.Name      = "lblFrameInfo";

            centerGroup.Controls.Add(timelineSlider);
            centerGroup.Controls.Add(lblFrameInfo);

            // Assemble bottom bar (Right before Fill so Fill gets remaining space)
            bottomBar.Controls.Add(centerGroup);
            bottomBar.Controls.Add(rightGroup);
            bottomBar.Controls.Add(leftGroup);

            // ── Form1 ─────────────────────────────────────────────────────
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode       = AutoScaleMode.Font;
            BackColor           = bgDark;
            ClientSize          = new Size(1280, 800);
            MinimumSize         = new Size(900, 600);
            Name  = "Form1";
            Text  = "MD5 Viewer";

            // Add controls: viewport first (behind), then overlays, then bar
            Controls.Add(viewport1);
            Controls.Add(titleLabel);
            Controls.Add(bottomBar);

            ((System.ComponentModel.ISupportInitialize)timelineSlider).EndInit();
            rightGroup.ResumeLayout(false);
            centerGroup.ResumeLayout(false);
            leftGroup.ResumeLayout(false);
            bottomBar.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        // ── fields ────────────────────────────────────────────────────────
        private Viewport        viewport1;
        private Panel           bottomBar;
        private FlowLayoutPanel leftGroup;
        private Button          btnReset;
        private Button          btnPlay;
        private ComboBox        animationCombo;
        private Panel           centerGroup;
        private TrackBar        timelineSlider;
        private Label           lblFrameInfo;
        private FlowLayoutPanel rightGroup;
        private ComboBox        renderModeCombo;
        private Label           titleLabel;
    }
}

using System.Windows.Forms;

namespace MD5Viewer
{
    public partial class Form1 : Form
    {
        private bool loadingAnimationList;
        private bool scrubbingTimeline;

        public Form1()
        {
            InitializeComponent();
            renderModeCombo.DataSource    = Enum.GetValues(typeof(RenderPhase));
            renderModeCombo.SelectedItem  = RenderPhase.FullRender;
            viewport1.FrameCallback       = UpdateTimeline;
            // Let the form preview keys so N/F can be used even when combobox has focus
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.N)
            {
                viewport1.ToggleNormalYFlip();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.F)
            {
                viewport1.ToggleFlipWinding();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LoadAnimationList();
            LoadCyberdemon();
        }

        // ── toolbar events ────────────────────────────────────────────────

        private void renderModeCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (renderModeCombo.SelectedItem is RenderPhase phase)
            {
                viewport1.Phase = phase;
                viewport1.Invalidate();
            }
        }

        private void animationCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!loadingAnimationList && animationCombo.SelectedItem != null)
                LoadCyberdemon();
        }

        private void btnPlay_Click(object sender, EventArgs e)
        {
            viewport1.IsPaused = !viewport1.IsPaused;
            btnPlay.Text       = viewport1.IsPaused ? "\u25B6" : "\u23F8"; // ▶ / ⏸
        }

        private void btnReset_Click(object sender, EventArgs e)
        {
            LoadCyberdemon();
        }

        private void timelineSlider_Scroll(object sender, EventArgs e)
        {
            if (viewport1.MD5Model == null) return;
            scrubbingTimeline  = true;
            viewport1.IsPaused = true;
            btnPlay.Text       = "\u25B6";
            viewport1.MD5Model.SeekToFrame(timelineSlider.Value);
            viewport1.MarkModelDirty();
            viewport1.Invalidate();
            scrubbingTimeline  = false;
        }

        // ── frame callback (called from Viewport render thread → UI thread) ──

        private void UpdateTimeline(int currentFrame, int totalFrames)
        {
            if (scrubbingTimeline || totalFrames < 2) return;

            // Marshal to UI thread safely
            if (InvokeRequired) { BeginInvoke(() => UpdateTimeline(currentFrame, totalFrames)); return; }

            timelineSlider.Maximum = totalFrames - 1;
            timelineSlider.Value   = Math.Clamp(currentFrame, 0, totalFrames - 1);
            lblFrameInfo.Text      = $"{currentFrame + 1} / {totalFrames}";
        }

        // ── model loading ─────────────────────────────────────────────────

        private void LoadCyberdemon()
        {
            string assetDir       = Path.Combine(AppContext.BaseDirectory, "Assets");
            string mesh           = Path.Combine(assetDir, "cyberdemon.md5mesh");
            string animationName  = animationCombo.SelectedItem?.ToString() ?? "idle.md5anim";
            string anim           = Path.Combine(assetDir, animationName);
            string diffuse        = File.Exists(Path.Combine(assetDir, "cyberdemon.tga"))
                                    ? Path.Combine(assetDir, "cyberdemon.tga")
                                    : Path.Combine(assetDir, "cyberdemon.jpg");
            string normal         = File.Exists(Path.Combine(assetDir, "cyberdemon_local.tga"))
                                    ? Path.Combine(assetDir, "cyberdemon_local.tga")
                                    : Path.Combine(assetDir, "cyberdemon_bmp.tga");
            string specular       = Path.Combine(assetDir, "cyberdemon_s.tga");

            MD5Model model = new MD5Model();
            model.Load(mesh);
            model.LoadAnim(animationName, anim);
            model.UseAnimation(animationName);

            viewport1.IsPaused = false;
            btnPlay.Text       = "\u23F8"; // ⏸
            viewport1.LoadScene(model, diffuse, normal, specular);

            string shortAnim = animationName.Replace(".md5anim", "");
            titleLabel.Text  = $"Cyberdemon  \u2014  {shortAnim}";
        }

        private void LoadAnimationList()
        {
            string   assetDir  = Path.Combine(AppContext.BaseDirectory, "Assets");
            string[] animations = Directory.GetFiles(assetDir, "*.md5anim")
                                           .Select(Path.GetFileName)
                                           .OrderBy(n => n)
                                           .ToArray();

            loadingAnimationList         = true;
            animationCombo.DataSource    = animations;
            animationCombo.SelectedItem  = animations.Contains("idle.md5anim")
                                           ? "idle.md5anim"
                                           : animations.FirstOrDefault();
            loadingAnimationList         = false;
        }
    }
}

namespace RAWeb.WebViewClient;

partial class Form1 {
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing) {
        if (disposing && (components != null)) {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent() {
        components = new System.ComponentModel.Container();
        AutoScaleMode = AutoScaleMode.Font;

        // window title
        Text = "RAWeb";

        // window size
        if (Screen.PrimaryScreen is not null) {
            var screen = Screen.PrimaryScreen.WorkingArea;
            Width = Math.Min(1600, Math.Min((int)(screen.Width * 0.8), (int)(screen.Height * 0.8)));
            Height = Width * 2 / 3; // 3:2 aspect ratio
            ClientSize = new Size(Width, Height);
        }
        else {
            ClientSize = new Size(800, 450);
        }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace SimHandling
{
    internal sealed class MainForm : Form
    {
        private const int HeaderHeight = 110;
        private const string About =
            "Physics Overhaul changes:\r\n"
            + "- Grip falloff\r\n"
            + "- Corner grip\r\n"
            + "- Tire stiffness\r\n"
            + "- Draft length\r\n"
            + "- Draft width\r\n"
            + "- Draft effect\r\n"
            + "- Center height\r\n"
            + "\r\n"
            + "Built-in assists turned off:\r\n"
            + "- Extra grip\r\n"
            + "- Steering help\r\n"
            + "- Racing line help\r\n"
            + "- Brake help\r\n"
            + "- Brake rotation help\r\n"
            + "- Wall help\r\n"
            + "- Stability help\r\n"
            + "\r\n"
            + "Still uses the game settings:\r\n"
            + "- Driving aid menu\r\n"
            + "- Garage setup\r\n"
            + "- Tire wear\r\n"
            + "- Draft strength\r\n"
            + "\r\n"
            + "Competition keeps those settings. It still forces expert aids, faster wear, and lower draft strength.\r\n"
            + "\r\n"
            + "Close Heat 5 first.\r\n"
            + "Apply writes this onto the selected folder.\r\n"
            + "Remove puts these values back.\r\n"
            + "Changes from other utilities are left in place.";
        private readonly ComboBox _builds;
        private readonly Label _status;
        private readonly Label _path;
        private readonly Icon _icon;
        private readonly Image? _logo;
        private readonly Image? _banner;
        private GameInstall[] _installs = Array.Empty<GameInstall>();

        public MainForm()
        {
            Text = "Physics Overhaul";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 720);
            BackColor = Theme.Asphalt;
            ForeColor = Theme.White;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9.5f);
            _logo = LoadImage("SimHandling.heat5-logo.png");
            _banner = LoadImage("SimHandling.banner-header.jpg");
            _icon = MakeIcon(LoadImage("SimHandling.icon-physics.jpg"));
            Icon = _icon;
            Paint += (_, e) => DrawHeader(e.Graphics);

            Controls.Add(new Label
            {
                Text = "BUILD",
                Location = new Point(28, HeaderHeight + 18),
                Size = new Size(80, 22),
                Font = new Font("Impact", 16, FontStyle.Regular, GraphicsUnit.Pixel),
                ForeColor = Theme.Yellow,
                BackColor = Theme.Asphalt
            });
            _builds = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(110, HeaderHeight + 16),
                Size = new Size(420, 28),
                BackColor = Theme.Track,
                ForeColor = Theme.White,
                FlatStyle = FlatStyle.Flat
            };
            _builds.SelectedIndexChanged += (_, __) => ShowSelection();
            Controls.Add(_builds);

            var add = new LinkLabel
            {
                Text = "Add build…",
                Location = new Point(28, HeaderHeight + 52),
                AutoSize = true,
                LinkColor = Theme.Yellow,
                ActiveLinkColor = Theme.White,
                VisitedLinkColor = Theme.Yellow,
                BackColor = Theme.Asphalt
            };
            add.LinkClicked += (_, __) => AddBuild();
            Controls.Add(add);

            var panel = new Panel
            {
                Location = new Point(20, HeaderHeight + 82),
                Size = new Size(520, 300),
                BackColor = Theme.Panel,
                AutoScroll = true
            };
            panel.Paint += (_, e) => Theme.PaintLeftAccent(e.Graphics, panel.Height);
            panel.Controls.Add(new Label
            {
                Location = new Point(12, 8),
                AutoSize = true,
                MaximumSize = new Size(480, 0),
                ForeColor = Theme.White,
                BackColor = Theme.Panel,
                Text = About
            });
            Controls.Add(panel);

            var apply = MakeButton("APPLY", Theme.Red, Theme.White, new Point(28, 508));
            apply.Click += (_, __) => { if (Ask("Apply Physics Overhaul to this build?")) DoApply(); };
            var remove = MakeButton("REMOVE", Color.FromArgb(24, 24, 26), Theme.Yellow, new Point(292, 508));
            remove.FlatAppearance.BorderColor = Theme.Yellow;
            remove.FlatAppearance.BorderSize = 2;
            remove.Click += (_, __) => { if (Ask("Remove Physics Overhaul from this build?")) DoRemove(); };
            Controls.Add(apply);
            Controls.Add(remove);

            _path = new Label
            {
                Location = new Point(28, 564),
                Size = new Size(504, 36),
                ForeColor = Theme.Mute,
                BackColor = Theme.Asphalt
            };
            Controls.Add(_path);
            _status = new Label
            {
                Location = new Point(28, 604),
                Size = new Size(504, 68),
                ForeColor = Theme.Mute,
                BackColor = Theme.Asphalt
            };
            Controls.Add(_status);
            Shown += (_, __) =>
            {
                Activate();
                BringToFront();
            };
            Reload();
        }

        private void Reload(string selectRoot = null)
        {
            var keep = selectRoot ?? Current()?.Root;
            _installs = Installs.Find().ToArray();
            _builds.Items.Clear();
            var index = 0;
            for (var i = 0; i < _installs.Length; i++)
            {
                _builds.Items.Add(_installs[i].Root);
                if (keep != null && string.Equals(_installs[i].Root, keep, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                }
            }

            var width = _builds.Width;
            using (var g = _builds.CreateGraphics())
            {
                foreach (var item in _builds.Items)
                {
                    width = Math.Max(width, (int)g.MeasureString(item.ToString(), _builds.Font).Width + 28);
                }
            }

            _builds.DropDownWidth = width;
            if (_builds.Items.Count > 0)
            {
                _builds.SelectedIndex = index;
            }
            else
            {
                _path.Text = "No build found.";
                _status.Text = "Add the NASCAR Heat 5 folder for the build you want.";
            }
        }

        private void ShowSelection()
        {
            var install = Current();
            if (install == null)
            {
                return;
            }

            _path.Text = install.Dll;
            try
            {
                var on = HandlingPatch.IsApplied(install.Dll);
                _status.ForeColor = on ? Theme.Yellow : Theme.Mute;
                _status.Text = on
                    ? "Physics Overhaul is on this build. The driving aid menu, garage, and track grip settings still apply."
                    : "Physics Overhaul is off this build.";
            }
            catch (Exception ex)
            {
                _status.ForeColor = Color.FromArgb(255, 120, 90);
                _status.Text = ex.Message;
            }
        }

        private void DoApply()
        {
            Run(HandlingPatch.Apply, "Physics Overhaul is on. The other tools still edit their own values.");
        }

        private void DoRemove()
        {
            Run(HandlingPatch.Remove, "Physics Overhaul was removed. Changes from other utilities were left in place.");
        }

        private void Run(Func<string, HandlingResult> action, string ok)
        {
            var install = Current();
            if (install == null)
            {
                return;
            }

            if (Process.GetProcessesByName("NASCARHeat5").Length > 0)
            {
                _status.ForeColor = Color.FromArgb(255, 120, 90);
                _status.Text = "NASCAR Heat 5 is running. Close it, then try again.";
                return;
            }

            try
            {
                action(install.Dll);
                _status.ForeColor = Theme.Yellow;
                _status.Text = ok;
                ShowSelection();
                _status.Text = ok;
            }
            catch (Exception ex)
            {
                _status.ForeColor = Color.FromArgb(255, 120, 90);
                _status.Text = ex.Message;
            }
        }

        private void AddBuild()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select the NASCAR Heat 5 folder.";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var root = Installs.Resolve(dialog.SelectedPath);
                var dll = string.IsNullOrEmpty(root) ? "" : Path.Combine(root, "NASCARHeat5_Data", "Managed", "Assembly-CSharp.dll");
                if (dll.Length == 0 || !File.Exists(dll))
                {
                    _status.Text = "That folder does not contain NASCAR Heat 5.";
                    return;
                }

                if (HandlingPatch.IsProtected(dll))
                {
                    _status.Text = "That folder is a mod pack. Pick the game install.";
                    return;
                }

                Installs.Remember(root);
                Reload(root);
            }
        }

        private GameInstall Current()
        {
            var index = _builds.SelectedIndex;
            if (index < 0 || index >= _installs.Length)
            {
                return null;
            }

            return _installs[index];
        }

        private bool Ask(string text)
        {
            return MessageBox.Show(this, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void DrawHeader(Graphics g)
        {
            var dest = new Rectangle(0, 0, ClientSize.Width, HeaderHeight);
            g.Clear(Color.Black);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            if (_banner != null && _banner.Width > 1 && _banner.Height > 1)
            {
                var scale = dest.Height / (float)_banner.Height;
                var drawnW = _banner.Width * scale;
                if (drawnW <= dest.Width + 0.5f)
                {
                    var w = Math.Max(1, (int)Math.Round(drawnW));
                    g.DrawImage(_banner, new Rectangle(dest.Right - w, dest.Y, w, dest.Height));
                }
                else
                {
                    var srcW = dest.Width / scale;
                    var srcX = _banner.Width - srcW;
                    g.DrawImage(_banner, dest, srcX, 0f, srcW, _banner.Height, GraphicsUnit.Pixel);
                }
            }

            var titleX = dest.X + 16f;
            if (_logo != null && _logo.Width > 0 && _logo.Height > 0)
            {
                var lh = dest.Height * 0.62f;
                var lw = lh * _logo.Width / _logo.Height;
                var maxW = dest.Width * 0.34f;
                if (lw > maxW)
                {
                    lw = maxW;
                    lh = lw * _logo.Height / _logo.Width;
                }

                var ly = dest.Y + (dest.Height - lh) / 2f;
                using (var attr = new ImageAttributes())
                {
                    attr.SetColorKey(Color.FromArgb(0, 0, 0), Color.FromArgb(18, 18, 18));
                    g.DrawImage(
                        _logo,
                        new Rectangle((int)(dest.X + 12), (int)ly, Math.Max(1, (int)lw), Math.Max(1, (int)lh)),
                        0,
                        0,
                        _logo.Width,
                        _logo.Height,
                        GraphicsUnit.Pixel,
                        attr);
                }

                titleX = dest.X + 20f + lw;
            }

            var slotW = Math.Max(48f, dest.Right - titleX - 12f);
            var size = Math.Max(12f, dest.Height * 0.28f);
            using (var font = new Font("Segoe UI", size, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                Theme.DrawOutlinedText(g, "Physics Overhaul", font, new RectangleF(titleX, dest.Y, slotW, dest.Height), format);
            }
        }

        private static Button MakeButton(string text, Color back, Color fore, Point loc)
        {
            return new Button
            {
                Text = text,
                Location = loc,
                Size = new Size(240, 42),
                BackColor = back,
                ForeColor = fore,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Impact", 16, FontStyle.Regular, GraphicsUnit.Pixel),
                Cursor = Cursors.Hand
            };
        }

        private static Image? LoadImage(string resource)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    return null;
                }

                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    copy.Position = 0;
                    using (var loaded = Image.FromStream(copy))
                    {
                        return new Bitmap(loaded);
                    }
                }
            }
        }

        private static Icon MakeIcon(Image? image)
        {
            if (image == null)
            {
                return (Icon)SystemIcons.Application.Clone();
            }

            var sizes = new[] { 16, 32, 48, 256 };
            var pngs = new byte[sizes.Length][];
            for (var i = 0; i < sizes.Length; i++)
            {
                var size = sizes[i];
                using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                using (var ms = new MemoryStream())
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.Clear(Color.Transparent);
                    g.DrawImage(image, new Rectangle(0, 0, size, size));
                    bmp.Save(ms, ImageFormat.Png);
                    pngs[i] = ms.ToArray();
                }
            }

            using (var ico = new MemoryStream())
            using (var writer = new BinaryWriter(ico))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)sizes.Length);
                var offset = 6 + (16 * sizes.Length);
                for (var i = 0; i < sizes.Length; i++)
                {
                    var size = sizes[i];
                    writer.Write((byte)(size >= 256 ? 0 : size));
                    writer.Write((byte)(size >= 256 ? 0 : size));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(pngs[i].Length);
                    writer.Write(offset);
                    offset += pngs[i].Length;
                }

                for (var i = 0; i < pngs.Length; i++)
                {
                    writer.Write(pngs[i]);
                }

                writer.Flush();
                using (var loaded = new Icon(new MemoryStream(ico.ToArray())))
                {
                    return (Icon)loaded.Clone();
                }
            }
        }
    }
}

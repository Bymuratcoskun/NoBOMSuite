using System;
using Gtk;

namespace NoBOMSuite.Desktop;

public class WelcomeWindow : Gtk.Window
{
    public WelcomeWindow(Gtk.Window parent)
    {
        SetTitle("DevGuard'a Hoş Geldiniz!");
        SetDefaultSize(550, 400);
        SetTransientFor(parent);
        SetModal(true);
        SetResizable(false);

        BuildUi();
    }

    private void BuildUi()
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 15);
        box.SetMarginStart(25);
        box.SetMarginEnd(25);
        box.SetMarginTop(25);
        box.SetMarginBottom(25);

        var title = Gtk.Label.New("");
        title.SetMarkup("<span size=\"18000\" weight=\"bold\">🛡️ DevGuard'a Hoş Geldiniz!</span>");
        title.SetHalign(Gtk.Align.Center);
        box.Append(title);

        var subtitle = Gtk.Label.New("DevGuard, kodunuzdaki görünmez hataları bulan ve yapay zeka ile onaran yeni nesil bir güvenlik asistanıdır. İşte hızlı bir başlangıç:");
        subtitle.SetWrap(true);
        subtitle.SetHalign(Gtk.Align.Center);
        box.Append(subtitle);

        var list = Gtk.Box.New(Gtk.Orientation.Vertical, 10);
        list.SetMarginTop(15);

        list.Append(CreateBulletRow("1. 📁 Dosyalarınızı Sürükleyin:", "Taramak istediğiniz dosya veya klasörleri ana ekrandaki alana sürükleyip bırakın."));
        list.Append(CreateBulletRow("2. ⚙️ Otomatik Onarım:", "Sağ üstteki 'Otomatik Onarım Modu' kutucuğunu işaretleyerek bulunan sorunların anında düzeltilmesini sağlayın."));
        list.Append(CreateBulletRow("3. 🪄 AI Çözüm Asistanı:", "Bir hata ile karşılaştığınızda, 'AI Çözüm' butonu ile yapay zekadan yardım isteyin. Ajanlarımız hatayı teşhis eder, çözer ve güvenlik onayı verir."));
        list.Append(CreateBulletRow("4. 📟 Canlı Konsol:", "Tüm işlemleri ve tarama sonuçlarını anlık olarak konsol sekmesinden takip edebilirsiniz."));
        box.Append(list);

        var btn = Gtk.Button.NewWithLabel("Anladım, Başlayalım!");
        btn.SetHalign(Gtk.Align.Center);
        btn.SetMarginTop(20);
        btn.AddCssClass("suggested-action");
        btn.OnClicked += (s, e) => this.Close();
        box.Append(btn);

        SetChild(box);
    }

    private Gtk.Widget CreateBulletRow(string titleText, string descriptionText)
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
        var labelTitle = Gtk.Label.New("");
        labelTitle.SetMarkup($"<b>{GLib.Markup.EscapeText(titleText)}</b>");
        labelTitle.SetHalign(Gtk.Align.Start);
        
        var labelDesc = Gtk.Label.New(descriptionText);
        labelDesc.SetWrap(true);
        labelDesc.SetHalign(Gtk.Align.Start);

        box.Append(labelTitle);
        box.Append(labelDesc);
        return box;
    }
}

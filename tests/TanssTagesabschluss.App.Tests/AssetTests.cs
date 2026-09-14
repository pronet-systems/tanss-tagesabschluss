using System.IO;
using Xunit;

namespace TanssTagesabschluss.App.Tests;

/// <summary>
/// Die Bilddateien der Anwendung müssen Bilddateien <b>bleiben</b>.
/// </summary>
/// <remarks>
/// <para><b>Dieser Test hat einen gemessenen Anlass.</b> Das Firmenlogo lag im Verzeichnis mit
/// der Kennfolge <c>89 50 4E 47 0A 1A 0A</c> statt <c>89 50 4E 47 0D 0A 1A 0A</c> — das
/// Wagenrücklauf-Byte fehlte. Eine Werkzeugkette, die die Datei für Text hält, wandelt
/// <c>CRLF</c> in <c>LF</c> und nimmt dabei genau dieses eine Byte mit. Alle vier Blöcke der
/// Datei blieben unversehrt, die Prüfsummen stimmten — nur der Kopf war kaputt.</para>
///
/// <para><b>Der Schaden ist deshalb so teuer, weil er still ist.</b> WPF wirft für eine
/// unlesbare <c>Image.Source</c> keine Ausnahme: Das Bild wird schlicht nicht gezeichnet, die
/// Seite baut sich ohne es auf, und alles andere daneben sieht richtig aus. Kein Fehler, keine
/// Meldung, keine Spur im Protokoll — nur eine Seite ohne Logo, die niemandem auffällt, der
/// nicht weiß, dass dort eines stehen soll.</para>
///
/// <para><b>Warum die Kennfolge und nicht <c>File.Exists</c>.</b> Vorhanden war die Datei die
/// ganze Zeit, und ihre Grösse stimmte bis aufs Byte. Geprüft wird deshalb das, was tatsächlich
/// fehlte.</para>
/// </remarks>
public sealed class AssetTests
{
    /// <summary>Die acht Bytes, mit denen jede PNG-Datei beginnt.</summary>
    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Das_Firmenlogo_ist_eine_lesbare_PNG_Datei()
    {
        byte[] head = new byte[PngSignature.Length];

        using (FileStream stream = File.OpenRead(Find("pronet-logo.png")))
        {
            Assert.Equal(head.Length, stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false));
        }

        Assert.Equal(PngSignature, head);
    }

    /// <summary>
    /// Sucht eine Datei unter <c>Assets</c> aufwärts vom Testverzeichnis.
    /// </summary>
    /// <remarks>
    /// Aufwärts statt mit einem fest gezählten Sprung: Wie tief unter <c>bin\</c> der Testläufer
    /// die Baugruppe ablegt, hängt am Zielframework und an der Konfiguration.
    /// </remarks>
    private static string Find(string name)
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);

        while (folder is not null)
        {
            string candidate = Path.Combine(
                folder.FullName, "src", "TanssTagesabschluss.App", "Assets", name);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            folder = folder.Parent;
        }

        throw new FileNotFoundException(
            $"{name} wurde von {AppContext.BaseDirectory} aufwärts nicht gefunden. Die Datei "
            + "gehört nach src\\TanssTagesabschluss.App\\Assets.");
    }
}

using GoodsManager.Web.Products;

namespace GoodsManager.Tests;

public sealed class IdentifierCodeTests
{
    [TestCase("isbn10", " ０-３０６-４０６１５-２　", "0306406152", "09780306406157")]
    [TestCase("isbn13", "978-0-306-40615-7", "9780306406157", "09780306406157")]
    [TestCase("ean13", "9780306406157", "9780306406157", "09780306406157")]
    [TestCase("isbn10", "0-8044-2957-x", "080442957X", "09780804429573")]
    [TestCase("isbn10", "0471384291", "0471384291", "09780471384298")]
    [TestCase("upca", "036000291452", "036000291452", "00036000291452")]
    [TestCase("ean13", "0036000291452", "0036000291452", "00036000291452")]
    [TestCase("ean8", "96385074", "96385074", "00000096385074")]
    [TestCase("ean13", "4006381333931", "4006381333931", "04006381333931")]
    [TestCase("isbn13", "9791090636071", "9791090636071", "09791090636071")]
    public void PreservesRawAndNormalizesOnlyTheAgreedCharacters(string scheme, string raw, string normalized, string comparison)
    {
        Assert.That(IdentifierCodes.TryNormalize(scheme, raw, out var result), Is.True);
        Assert.That(result, Is.EqualTo(new IdentifierValue(scheme, raw, normalized, comparison)));
    }

    [TestCase("isbn10", "0306406153")]
    [TestCase("isbn10", "X804429570")]
    [TestCase("isbn10", "080442957Ｘ")]
    [TestCase("isbn13", "4006381333931")]
    [TestCase("ean13", "4006381333932")]
    [TestCase("ean8", "96385075")]
    [TestCase("upca", "036000291453")]
    [TestCase("upca", "0036000291452")]
    [TestCase("gtin14", "00036000291452")]
    [TestCase("EAN13", "4006381333931")]
    [TestCase("ean13", "４００６３８１３３３９３１\t")]
    [TestCase("ean13", "4006381\n333931")]
    [TestCase("ean13", "4006381\u00a0333931")]
    [TestCase("ean13", "4006381‐333931")]
    [TestCase("ean13", "4006381333931\0")]
    [TestCase("ean13", "٤006381333931")]
    [TestCase("ean13", "4006381333931extra")]
    [TestCase("ean13", " -　 ")]
    [TestCase("ean13", null)]
    [TestCase(null, "4006381333931")]
    public void RejectsInvalidChecksumsKindsLengthsAndUnapprovedCharacters(string? scheme, string? raw)
    {
        Assert.That(IdentifierCodes.TryNormalize(scheme, raw, out var result), Is.False);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void RejectsBrokenUtf16AndDoesNotSilentlyDiscardIt()
    {
        Assert.That(IdentifierCodes.TryNormalize("ean13", "4006381333931" + '\ud800', out _), Is.False);
    }

    [Test]
    public void EverySingleDigitMutationFailsChecksumForEachScheme()
    {
        foreach (var (scheme, code) in new[] { ("ean8", "96385074"), ("upca", "036000291452"), ("ean13", "4006381333931"), ("isbn10", "0306406152"), ("isbn13", "9780306406157") })
        {
            for (var index = 0; index < code.Length; index++)
            {
                for (var digit = '0'; digit <= '9'; digit++)
                {
                    if (digit == code[index]) continue;
                    var changed = code.ToCharArray();
                    changed[index] = digit;
                    Assert.That(IdentifierCodes.TryNormalize(scheme, new string(changed), out _), Is.False, $"{scheme}: {new string(changed)}");
                }
            }
        }
    }
}

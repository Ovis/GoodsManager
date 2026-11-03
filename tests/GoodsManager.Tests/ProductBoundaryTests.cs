using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;

namespace GoodsManager.Tests;

/// <summary>所持数量の安全な集計範囲と、不正な保存値の拒否を検証する。</summary>
public sealed class ProductBoundaryTests
{
    /// <summary>Int64で集計し、APIで安全に扱えない値・桁あふれ・不正数量を拒否する。</summary>
    [Test]
    public void TotalsUseInt64AndRejectUnsafeNumbersAndOverflow()
    {
        Assert.That(QuantityTotals.Calculate([int.MaxValue, int.MaxValue], 1), Is.EqualTo(4294967295L));
        Assert.That(QuantityTotals.Calculate([], StorageValues.MaximumSafeInteger), Is.EqualTo(StorageValues.MaximumSafeInteger));

        foreach (var itemCount in new[] { StorageValues.MaximumSafeInteger + 1, long.MaxValue })
        {
            Assert.That(Assert.Throws<MasterProblem>(() => QuantityTotals.Calculate([1], itemCount))!.Code, Is.EqualTo("QUANTITY_LIMIT_EXCEEDED"));
        }

        Assert.Throws<MasterProblem>(() => QuantityTotals.Calculate(Enumerable.Repeat(int.MaxValue, 4194305), 0));
        Assert.Throws<InvalidDataException>(() => QuantityTotals.Calculate([0], 0));
    }
}

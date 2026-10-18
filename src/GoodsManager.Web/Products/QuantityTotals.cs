using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;

namespace GoodsManager.Web.Products;

public static class QuantityTotals
{
    public static long Calculate(IEnumerable<int> quantities, long itemCount)
    {
        try
        {
            if (itemCount < 0) throw new OverflowException();
            long sum = 0;
            foreach (var quantity in quantities)
            {
                if (quantity < 1) throw new InvalidDataException("ロット数量の保存値が不正");
                sum = checked(sum + quantity);
            }
            return StorageValues.RequireSafeTotal(checked(sum + itemCount));
        }
        catch (OverflowException) { throw new MasterProblem(409, "QUANTITY_LIMIT_EXCEEDED"); }
    }
}

using OrderRouting.Api.Data;
using OrderRouting.Api.Models;

namespace OrderRouting.Api.Routing;

public sealed class OrderRouter
{
    private readonly CsvSupplierRepository _suppliers;
    private readonly decimal _localRatingSimilarityDelta;

    public OrderRouter(CsvSupplierRepository suppliers, IConfiguration configuration)
    {
        _suppliers = suppliers;
        _localRatingSimilarityDelta = configuration.GetValue("Routing:LocalRatingSimilarityDelta", 0.5m);
    }

    public RouteOrderResponse Route(ValidatedOrder order)
    {
        var candidateLists = new List<ItemCandidates>();
        var errors = new List<string>();

        foreach (var item in order.Items)
        {
            var candidates = _suppliers.All
                .Select(supplier => SupplierEligibility.GetCandidate(supplier, item.Product, order.CustomerZip, order.MailOrder))
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .GroupBy(candidate => candidate.Supplier.SupplierId, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(candidate => candidate.IsLocal).First())
                .OrderByDescending(candidate => candidate.IsLocal)
                .ThenByDescending(candidate => RatingValue(candidate.Supplier))
                .ThenBy(candidate => candidate.Supplier.SupplierId, StringComparer.Ordinal)
                .ToArray();

            if (candidates.Length == 0)
            {
                errors.Add($"No eligible supplier can fulfill product {item.ProductCode} for category {item.Product.Category} at ZIP {order.CustomerZip}.");
            }
            else
            {
                candidateLists.Add(new ItemCandidates(item, candidates));
            }
        }

        if (errors.Count > 0)
        {
            return RouteOrderResponse.Failure(errors);
        }

        var plan = FindBestPlan(candidateLists);
        if (plan is null)
        {
            return RouteOrderResponse.Failure(["No feasible routing plan could be produced for this order."]);
        }

        var routing = plan.Assignments
            .GroupBy(assignment => assignment.Candidate.Supplier.SupplierId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var supplier = group.First().Candidate.Supplier;
                var items = group
                    .OrderBy(assignment => assignment.Item.RequestIndex)
                    .Select(assignment => new RouteItemResponse(
                        assignment.Item.ProductCode,
                        assignment.Item.Quantity,
                        assignment.Item.Product.Category,
                        assignment.Candidate.FulfillmentMode))
                    .ToArray();

                return new RouteSupplierResponse(supplier.SupplierId, supplier.SupplierName, items);
            })
            .ToArray();

        return RouteOrderResponse.Success(routing);
    }

    private RoutingPlan? FindBestPlan(IReadOnlyList<ItemCandidates> candidateLists)
    {
        var orderedItems = candidateLists
            .OrderBy(item => item.Candidates.Count)
            .ThenBy(item => item.Item.RequestIndex)
            .ToArray();

        RoutingPlan? best = null;
        Search(0, orderedItems, [], ref best);
        return best;
    }

    private void Search(int depth, IReadOnlyList<ItemCandidates> orderedItems, List<ItemAssignment> assignments, ref RoutingPlan? best)
    {
        if (depth == orderedItems.Count)
        {
            var plan = new RoutingPlan(assignments.ToArray());
            if (best is null || ComparePlans(plan, best) < 0)
            {
                best = plan;
            }

            return;
        }

        var usedShipments = assignments.Select(assignment => assignment.Candidate.Supplier.SupplierId).Distinct(StringComparer.Ordinal).Count();
        if (best is not null && usedShipments > best.ShipmentCount)
        {
            return;
        }

        var itemCandidates = orderedItems[depth];
        foreach (var candidate in itemCandidates.Candidates)
        {
            assignments.Add(new ItemAssignment(itemCandidates.Item, candidate));
            var nextShipments = assignments.Select(assignment => assignment.Candidate.Supplier.SupplierId).Distinct(StringComparer.Ordinal).Count();

            if (best is null || nextShipments <= best.ShipmentCount)
            {
                Search(depth + 1, orderedItems, assignments, ref best);
            }

            assignments.RemoveAt(assignments.Count - 1);
        }
    }

    private int ComparePlans(RoutingPlan left, RoutingPlan right)
    {
        var shipmentComparison = left.ShipmentCount.CompareTo(right.ShipmentCount);
        if (shipmentComparison != 0)
        {
            return shipmentComparison;
        }

        var ratingDifference = left.WeightedRating - right.WeightedRating;
        if (Math.Abs(ratingDifference) <= _localRatingSimilarityDelta)
        {
            var localComparison = right.LocalItemCount.CompareTo(left.LocalItemCount);
            if (localComparison != 0)
            {
                return localComparison;
            }
        }

        if (ratingDifference != 0)
        {
            return ratingDifference > 0 ? -1 : 1;
        }

        return string.CompareOrdinal(left.SupplierTieBreakKey, right.SupplierTieBreakKey);
    }

    private static decimal RatingValue(Supplier supplier) => supplier.CustomerSatisfactionScore ?? 0m;
}

internal sealed record ItemCandidates(ValidatedOrderItem Item, IReadOnlyList<SupplierCandidate> Candidates);

internal sealed record ItemAssignment(ValidatedOrderItem Item, SupplierCandidate Candidate);

internal sealed class RoutingPlan
{
    public RoutingPlan(IReadOnlyList<ItemAssignment> assignments)
    {
        Assignments = assignments;
        ShipmentCount = assignments.Select(assignment => assignment.Candidate.Supplier.SupplierId).Distinct(StringComparer.Ordinal).Count();
        var totalQuantity = assignments.Sum(assignment => assignment.Item.Quantity);
        WeightedRating = totalQuantity == 0
            ? 0m
            : assignments.Sum(assignment => (assignment.Candidate.Supplier.CustomerSatisfactionScore ?? 0m) * assignment.Item.Quantity) / totalQuantity;
        LocalItemCount = assignments.Count(assignment => assignment.Candidate.IsLocal);
        SupplierTieBreakKey = string.Join("|", assignments.Select(assignment => assignment.Candidate.Supplier.SupplierId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    public IReadOnlyList<ItemAssignment> Assignments { get; }

    public int ShipmentCount { get; }

    public decimal WeightedRating { get; }

    public int LocalItemCount { get; }

    public string SupplierTieBreakKey { get; }
}

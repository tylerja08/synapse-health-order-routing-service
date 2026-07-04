# Requirements #

## Order Routing Service Description ##
The order routing service routes orders to suppliers based on a set of business rules.

## Core Service Functionality ## 
- Accepts an order with multiple line items. [Example](../test_data/sample_orders.json)
- Reads supplier and product data from [suppliers.csv](../service_data/suppliers.csv) and [products.csv](../service_data/products.csv)
- Routing API that considers:  
  - Product capabilities:
    - Can the supplier fulfill this product category? 
  - Geographic coverage:
    - Does the supplier serve this ZIP code? (unless 
mail_order is true) 
  - Mail order eligibility:
    - If order allows mail order, consider suppliers with 
can_mail_order=y
  - Customer experience:
    - Minimize number of shipments when possible 
  - Quality considerations:
    - Factor in customer satisfaction scores 
- Returns a routing decision showing which items go to which suppliers


## Routing Logic Priorities ##
The routing api should optimize for (in order of importance):
1. Feasibility: 
   - Only route to suppliers who can actually fulfill the items
2. Customer experience:
   - Prefer fewer shipments (consolidate with one supplier 
when possible)
3. Quality:
   - When multiple options exist, prefer higher-rated suppliers
4. Geographic preference:
   - Prefer local suppliers over mail-order when ratings are 
similar


## Endpoint Format ##

`POST /api/route` - The order routing endpoint

Example order request payload: 
```
{ 
  "order_id": "ORD-EXAMPLE", 
  "customer_zip": "10015", 
  "mail_order": false, 
  "items": [ 
    { "product_code": "WC-STD-001", "quantity": 1 }, 
    { "product_code": "OX-PORT-024", "quantity": 1 } 
  ] 
}
```
 
`POST /api/route` - always returns HTTP 200. The feasible field tells you whether 
routing succeeded. 
 
Successful Routing Example 
```
{ 
  "feasible": true, 
  "routing": [ 
    { 
      "supplier_id": "SUP-005", 
      "supplier_name": "Respiratory Care Co Co", 
      "items": [ 
        { 
          "product_code": "WC-STD-001", 
          "quantity": 1, 
          "category": "wheelchair", 
          "fulfillment_mode": "local" 
        } 
      ] 
    } 
  ] 
} 
```
 
Unsuccessful Routing Example 
```
{ 
  "feasible": false, 
  "errors": [ 
    "Order must include at least one line item.", 
    "Order must include a valid customer_zip." 
  ] 
}
```

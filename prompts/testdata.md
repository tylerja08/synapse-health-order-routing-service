# Test Data #

## Document Mapping ##
- [suppliers.csv](../docs/suppliers.csv)
  - List of suppliers with their capabilities
  - Detailed information located at [Supplier Data Structure](#supplier-data-structure)
- [products.csv](../docs/products.csv)
  - List of 200 products mapping product codes to categories
- [sample_orders.json](../docs/sample_orders.json)
  - 3 example orders to test our routing logic

### Supplier Data Structure ###
The [suppliers.csv](../docs/suppliers.csv) contains realistic, somewhat messy data:
- **supplier_id:** Unique identifier (e.g., "SUP-001")
- **supplier_name:** Business name
- **service_zips:** ZIP codes served
  - Explicit list: "10001, 10002, 10003"
  - Range: "10001-10100"
- **product_categories:** Comma-separated list of categories they handle
- **customer_satisfaction_score:** Rating 1-10, or "no ratings yet"
- **can_mail_order?:** "y" or "n"
  - whether they ship nationally

### Order Details ###
- When an order’s `mail_order` attribute is false, only route to suppliers serving the 
customer's ZIP code
- When `mail_order` is true, any supplier with can_mail_order? = "y" is 
eligible regardless of ZIP code.
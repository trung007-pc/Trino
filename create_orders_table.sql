-- Create orders table for JOIN testing
CREATE TABLE IF NOT EXISTS iceberg.v4.orders (
    Id VARCHAR,
    CustomerId VARCHAR,
    OrderDate TIMESTAMP,
    Amount DECIMAL(18, 2),
    Status VARCHAR
)
WITH (
    format = 'PARQUET'
);

-- Verify table created
SHOW TABLES IN iceberg.v4;

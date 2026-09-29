export interface CatalogProduct {
  code: string;
  name: string;
  price: number;
}

/**
 * Suggestions for the product field. The API accepts any product code and price; this list only
 * saves typing, and picking a product pre-fills its price (still editable).
 */
export const SAMPLE_PRODUCTS: readonly CatalogProduct[] = [
  { code: 'CHR-ERGO-BLK', name: 'Ergonomic office chair, black', price: 189 },
  { code: 'DSK-OAK-160', name: 'Oak desk, 160 cm', price: 640 },
  { code: 'LMP-LED-ARM', name: 'LED desk lamp with arm', price: 24.5 },
  { code: 'MON-27-4K', name: '27-inch 4K monitor', price: 329 },
  { code: 'KBD-MECH-TKL', name: 'Mechanical keyboard, compact', price: 89.9 },
  { code: 'CBL-USBC-2M', name: 'USB-C cable, 2 m', price: 12.99 },
];

export const findProduct = (code: string): CatalogProduct | undefined =>
  SAMPLE_PRODUCTS.find((product) => product.code.toLowerCase() === code.trim().toLowerCase());

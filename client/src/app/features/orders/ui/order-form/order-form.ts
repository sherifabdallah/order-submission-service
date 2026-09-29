import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormArray,
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { map } from 'rxjs';
import { MoneyPipe } from '../../../../shared/ui/format';
import { Icon, IconName } from '../../../../shared/ui/icon';
import { OrderRules, PlaceOrderRequest } from '../../data-access/order.models';
import { AUTO_RETRIES, OrderSubmissionStore } from '../../data-access/order-submission-store';
import { SAMPLE_PRODUCTS, findProduct } from '../../data-access/sample-products';

type ItemForm = FormGroup<{
  productCode: FormControl<string>;
  quantity: FormControl<number | null>;
  unitPrice: FormControl<string>;
}>;

interface ItemValues {
  productCode: string;
  quantity: number | null;
  unitPrice: string;
}

interface OrderValues {
  customerReference: string;
  items: ItemValues[];
}

interface Notice {
  tone: 'warn' | 'bad';
  icon: IconName;
  title: string;
  text: string;
  canRetry: boolean;
  canDiscard: boolean;
  key?: string;
}

const EMPTY_ORDER: OrderValues = {
  customerReference: '',
  items: [{ productCode: '', quantity: 1, unitPrice: '' }],
};

const EXAMPLE_ORDER: OrderValues = {
  customerReference: 'ACME-4471',
  items: [
    { productCode: 'CHR-ERGO-BLK', quantity: 2, unitPrice: '189.00' },
    { productCode: 'DSK-OAK-160', quantity: 1, unitPrice: '640.00' },
    { productCode: 'LMP-LED-ARM', quantity: 3, unitPrice: '24.50' },
  ],
};

const PRICE_PATTERN = /^\d{1,7}(\.\d{1,2})?$/;

const parsePrice = (value: string): number => Number(value.replace(/,/g, '').trim());

const notBlank = (control: AbstractControl<string>): ValidationErrors | null =>
  control.value && control.value.trim().length === 0 ? { required: true } : null;

const priceValidator = (control: AbstractControl<string>): ValidationErrors | null => {
  const raw = (control.value ?? '').replace(/,/g, '').trim();
  if (!raw) {
    return null; // "required" reports empty values
  }
  if (!PRICE_PATTERN.test(raw)) {
    return /^\d+\.\d{3,}$/.test(raw) ? { decimals: true } : { price: true };
  }
  const value = Number(raw);
  if (value <= 0) return { min: { min: 0.01 } };
  if (value > OrderRules.maxUnitPrice) return { max: { max: OrderRules.maxUnitPrice } };
  return null;
};

const wholeNumber = (control: AbstractControl<number | null>): ValidationErrors | null =>
  control.value != null && !Number.isInteger(control.value) ? { integer: true } : null;

@Component({
  selector: 'app-order-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, Icon, MoneyPipe],
  templateUrl: './order-form.html',
  styleUrl: './order-form.scss',
})
export class OrderForm {
  protected readonly store = inject(OrderSubmissionStore);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly host = inject(ElementRef<HTMLElement>);

  protected readonly rules = OrderRules;
  protected readonly products = SAMPLE_PRODUCTS;
  protected readonly maxAttempts = AUTO_RETRIES + 1;

  protected readonly form = this.fb.group({
    customerReference: this.fb.control('', [Validators.required, Validators.maxLength(OrderRules.customerReferenceMaxLength), notBlank]),
    items: this.fb.array<ItemForm>([]),
  });

  protected get items(): FormArray<ItemForm> {
    return this.form.controls.items;
  }

  /** Errors are shown once a field was touched or after the first submit attempt. */
  protected readonly attempted = signal(false);

  private readonly values = toSignal(this.form.valueChanges.pipe(map(() => this.form.getRawValue())), {
    initialValue: this.form.getRawValue(),
  });

  protected readonly draft = computed(() => toRequest(this.values()));
  protected readonly keyPlan = computed(() => this.store.keyPlan(this.draft()));
  protected readonly locked = computed(() => this.store.sending());
  protected readonly lineCount = computed(() => this.values().items.length);

  protected readonly estimate = computed(() =>
    this.values().items.reduce((sum, item) => {
      const price = parsePrice(item.unitPrice);
      return Number.isFinite(price) && item.quantity ? sum + item.quantity * price : sum;
    }, 0),
  );

  protected readonly notice = computed<Notice | null>(() => {
    const error = this.store.error();
    const pending = this.store.pending();

    if (this.store.interrupted() && pending) {
      return {
        tone: 'warn',
        icon: 'alert',
        title: 'This order was not confirmed',
        text: 'You started placing it earlier, but no confirmation came back. It is restored below. Trying again is safe: it cannot create a second order.',
        canRetry: true,
        canDiscard: true,
        key: pending.key,
      };
    }

    if (this.store.phase() !== 'failed' || !error) {
      return null;
    }

    switch (error.kind) {
      case 'validation':
        return { tone: 'bad', icon: 'alert', title: 'Check the highlighted fields', text: 'Some values were not accepted.', canRetry: false, canDiscard: false };
      case 'conflict':
        return { tone: 'bad', icon: 'alert', title: 'This request was already used for a different order', text: error.message, canRetry: false, canDiscard: false };
      case 'rate-limited':
        return { tone: 'warn', icon: 'clock', title: 'Too many orders in a short time', text: 'Wait a few seconds, then try again.', canRetry: pending !== null, canDiscard: false, key: pending?.key };
      default:
        return {
          tone: 'warn',
          icon: 'alert',
          title: 'We could not confirm your order',
          text: `${error.message} The order may or may not have been placed. Trying again is safe: it cannot create a second order.`,
          canRetry: pending !== null,
          canDiscard: false,
          key: pending?.key,
        };
    }
  });

  constructor() {
    const pending = this.store.pending();
    this.load(pending ? fromRequest(pending.request) : EMPTY_ORDER);

    effect(() => {
      const locked = this.locked();
      untracked(() => (locked ? this.form.disable({ emitEvent: false }) : this.form.enable({ emitEvent: false })));
    });

    // Put the server's field-level validation messages on the matching controls.
    effect(() => {
      const error = this.store.error();
      if (error?.kind === 'validation') {
        untracked(() => {
          this.applyServerErrors(error.fieldErrors);
          this.focusFirstInvalid();
        });
      }
    });
  }

  protected useExample(): void {
    this.load(EXAMPLE_ORDER);
    this.attempted.set(false);
  }

  protected clear(): void {
    this.load(EMPTY_ORDER);
    this.attempted.set(false);
  }

  protected addItem(): void {
    if (this.items.length < OrderRules.maxLines) {
      this.items.push(this.createItem({ productCode: '', quantity: 1, unitPrice: '' }));
      this.focus(`#code-${this.items.length - 1}`);
    }
  }

  protected removeItem(index: number): void {
    if (this.items.length > 1) {
      this.items.removeAt(index);
    }
  }

  protected step(index: number, delta: number): void {
    const control = this.items.at(index).controls.quantity;
    const next = Math.min(Math.max((control.value ?? 0) + delta, 1), OrderRules.maxQuantity);
    control.setValue(next);
    control.markAsTouched();
  }

  /** Picking a known product fills in its price, unless the user already typed one. */
  protected applyCatalogPrice(index: number): void {
    const item = this.items.at(index).controls;
    const product = findProduct(item.productCode.value);
    if (product && !item.unitPrice.value.trim()) {
      item.unitPrice.setValue(product.price.toFixed(2));
    }
  }

  protected formatPrice(index: number): void {
    const control = this.items.at(index).controls.unitPrice;
    if (!control.errors && control.value.trim()) {
      control.setValue(parsePrice(control.value).toFixed(2));
    }
  }

  protected productName(index: number): string | null {
    return findProduct(this.values().items[index]?.productCode ?? '')?.name ?? null;
  }

  protected amount(index: number): number | null {
    const item = this.values().items[index];
    const price = parsePrice(item?.unitPrice ?? '');
    return item?.quantity && Number.isFinite(price) && item.unitPrice.trim() ? item.quantity * price : null;
  }

  protected submit(): void {
    this.attempted.set(true);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalid();
      return;
    }
    this.store.submit(this.draft());
  }

  protected retry(): void {
    this.store.retry();
  }

  protected discard(): void {
    this.store.discard();
    this.load(EMPTY_ORDER);
  }

  protected error(control: AbstractControl): string | null {
    const errors = control.errors;
    if (!errors || !(control.touched || this.attempted() || errors['server'])) {
      return null;
    }
    if (errors['server']) return errors['server'];
    if (errors['required']) return 'Required';
    if (errors['maxlength']) return `Use ${errors['maxlength'].requiredLength} characters or fewer`;
    if (errors['integer']) return 'Use a whole number';
    if (errors['decimals']) return 'Use at most 2 decimal places';
    if (errors['price']) return 'Enter a price such as 24.50';
    if (errors['min']) return `Must be at least ${errors['min'].min}`;
    if (errors['max']) return `Must be at most ${errors['max'].max.toLocaleString('en-US')}`;
    return 'Check this value';
  }

  private load(order: OrderValues): void {
    this.items.clear({ emitEvent: false });
    order.items.forEach((item) => this.items.push(this.createItem(item), { emitEvent: false }));
    this.form.controls.customerReference.setValue(order.customerReference, { emitEvent: false });
    this.form.markAsUntouched();
    this.form.updateValueAndValidity();
  }

  private createItem(values: ItemValues): ItemForm {
    return this.fb.group({
      productCode: this.fb.control(values.productCode, [Validators.required, Validators.maxLength(OrderRules.productCodeMaxLength), notBlank]),
      quantity: new FormControl<number | null>(values.quantity, [Validators.required, Validators.min(1), Validators.max(OrderRules.maxQuantity), wholeNumber]),
      unitPrice: this.fb.control(values.unitPrice, [Validators.required, priceValidator]),
    });
  }

  private applyServerErrors(fieldErrors: Record<string, string[]>): void {
    for (const [path, messages] of Object.entries(fieldErrors)) {
      const control = this.controlForPath(path);
      control?.setErrors({ ...control.errors, server: messages[0] });
      control?.markAsTouched();
    }
  }

  /** Maps "items[1].unitPrice" from the API's problem details onto the matching form control. */
  private controlForPath(path: string): AbstractControl | null {
    const match = /^items\[(\d+)\]\.(\w+)$/.exec(path);
    if (match) {
      return this.items.at(Number(match[1]))?.get(match[2]) ?? null;
    }
    return this.form.get(path);
  }

  private focusFirstInvalid(): void {
    this.focus('.input.invalid');
  }

  private focus(selector: string): void {
    setTimeout(() => (this.host.nativeElement as HTMLElement).querySelector<HTMLElement>(selector)?.focus());
  }
}

function toRequest(values: OrderValues): PlaceOrderRequest {
  return {
    customerReference: values.customerReference.trim(),
    items: values.items.map((item) => ({
      productCode: item.productCode.trim(),
      quantity: item.quantity ?? 0,
      unitPrice: Number.isFinite(parsePrice(item.unitPrice)) ? parsePrice(item.unitPrice) : 0,
    })),
  };
}

function fromRequest(request: PlaceOrderRequest): OrderValues {
  return {
    customerReference: request.customerReference,
    items: request.items.map((item) => ({ productCode: item.productCode, quantity: item.quantity, unitPrice: item.unitPrice.toFixed(2) })),
  };
}

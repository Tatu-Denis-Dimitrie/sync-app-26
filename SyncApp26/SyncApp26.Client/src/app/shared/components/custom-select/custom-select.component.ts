import { Component, ElementRef, EventEmitter, HostListener, Input, Output, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface SelectOption {
  value: string;
  label: string;
}

/**
 * Drop-in replacement for a native <select> where the option list has to stay short.
 * A native popup sizes itself and ignores CSS height, so a long registry (functions,
 * departments) fills the viewport; this one caps the panel and scrolls inside it.
 *
 * The empty string is the "nothing picked" value, shown as `placeholder`.
 */
@Component({
  selector: 'app-custom-select',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './custom-select.component.html'
})
export class CustomSelectComponent {
  @Input() options: SelectOption[] = [];
  @Input() value = '';
  @Input() placeholder = '(No change)';
  @Input() ariaLabel = '';
  @Output() valueChange = new EventEmitter<string>();

  @ViewChild('panel') panel?: ElementRef<HTMLElement>;

  isOpen = false;
  /** Option the keyboard is sitting on; -1 is the placeholder row. */
  activeIndex = -1;

  constructor(private host: ElementRef<HTMLElement>) { }

  get selectedLabel(): string {
    return this.options.find(o => o.value === this.value)?.label || this.placeholder;
  }

  get hasSelection(): boolean {
    return !!this.value;
  }

  toggle(): void {
    if (this.isOpen) {
      this.close();
    } else {
      this.open();
    }
  }

  open(): void {
    this.isOpen = true;
    this.activeIndex = this.options.findIndex(o => o.value === this.value);
    // The dialog body is itself scrollable, so a panel opened near its bottom edge
    // would otherwise be cut off rather than pushed into view.
    setTimeout(() => this.panel?.nativeElement.scrollIntoView({ block: 'nearest' }));
  }

  close(): void {
    this.isOpen = false;
    this.activeIndex = -1;
  }

  select(value: string): void {
    this.value = value;
    this.valueChange.emit(value);
    this.close();
  }

  onKeydown(event: KeyboardEvent): void {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (!this.isOpen) {
          this.open();
        } else {
          this.moveActive(this.activeIndex + 1);
        }
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (this.isOpen) {
          this.moveActive(this.activeIndex - 1);
        }
        break;
      case 'Enter':
      case ' ':
        event.preventDefault();
        if (!this.isOpen) {
          this.open();
        } else {
          this.select(this.activeIndex >= 0 ? this.options[this.activeIndex].value : '');
        }
        break;
      case 'Escape':
        if (this.isOpen) {
          event.preventDefault();
          this.close();
        }
        break;
      case 'Tab':
        this.close();
        break;
    }
  }

  private moveActive(index: number): void {
    // -1 keeps the placeholder reachable as the row above the first option.
    this.activeIndex = Math.max(-1, Math.min(index, this.options.length - 1));
    setTimeout(() => {
      this.panel?.nativeElement
        .querySelector(`[data-index="${this.activeIndex}"]`)
        ?.scrollIntoView({ block: 'nearest' });
    });
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.isOpen && !this.host.nativeElement.contains(event.target as Node)) {
      this.close();
    }
  }
}

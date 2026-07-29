import { Component, Input } from '@angular/core';
import { SignatureVerificationStatusValue } from '../../services/signature-verification.service';

@Component({
  selector: 'app-signature-status-badge',
  standalone: true,
  imports: [],
  template: `
    <span
      class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium"
      [class]="statusClass"
      >
      {{ statusLabel }}
    </span>
    `,
  styles: []
})
export class SignatureStatusBadgeComponent {
  @Input() status: SignatureVerificationStatusValue | null | undefined;

  get statusClass(): string {
    switch (this.status) {
      case 'Valid': return 'bg-green-100 text-green-800';
      case 'Invalid': return 'bg-red-100 text-red-800';
      case 'ChainBroken': return 'bg-orange-100 text-orange-800';
      case 'Legacy': return 'bg-gray-200 text-gray-500';
      case 'NotFound': return 'bg-gray-100 text-gray-800';
      default: return 'bg-gray-100 text-gray-800';
    }
  }

  get statusLabel(): string {
    switch (this.status) {
      case 'Valid': return 'Valid';
      case 'Invalid': return 'Invalid';
      case 'ChainBroken': return 'Chain Broken';
      case 'Legacy': return 'Legacy';
      case 'NotFound': return 'Not Found';
      default: return 'Unknown';
    }
  }
}

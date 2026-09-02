import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslationService } from '../../services/translation.service';
import { OrgNameKind, orgNameLabel } from '../utils/org-name.util';

// Localizes a department or function name for display. Impure, like TranslatePipe, so it re-runs
// when the active language changes.
@Pipe({
  name: 'orgName',
  standalone: true,
  pure: false
})
export class OrgNamePipe implements PipeTransform {
  private translationService = inject(TranslationService);

  transform(name: string | null | undefined, kind: OrgNameKind): string {
    return orgNameLabel(kind, name, key => this.translationService.translate('Common', key));
  }
}

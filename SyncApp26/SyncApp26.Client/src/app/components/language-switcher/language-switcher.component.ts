import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LANGUAGE_LABELS, Language, SUPPORTED_LANGUAGES, TranslationService } from '../../services/translation.service';

@Component({
  selector: 'app-language-switcher',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './language-switcher.component.html'
})
export class LanguageSwitcherComponent {
  readonly languages = SUPPORTED_LANGUAGES;
  readonly labels = LANGUAGE_LABELS;
  isOpen = false;

  constructor(private translationService: TranslationService) {}

  get currentLanguage(): Language {
    return this.translationService.language();
  }

  ariaLabel(): string {
    return this.translationService.translate('Common', 'languageSwitcher.ariaLabel', this.labels[this.currentLanguage]);
  }

  toggle(): void {
    this.isOpen = !this.isOpen;
  }

  close(): void {
    this.isOpen = false;
  }

  select(language: Language): void {
    this.close();
    if (language === this.currentLanguage) {
      return;
    }
    
    this.translationService.setLanguage(language).subscribe();
  }
}

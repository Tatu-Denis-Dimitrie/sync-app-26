import { Component, OnInit } from '@angular/core';

import { VersionService } from '../../services/version.service';

@Component({
  selector: 'app-footer',
  standalone: true,
  imports: [],
  templateUrl: './footer.component.html',
  styleUrl: './footer.component.css'
})
export class FooterComponent implements OnInit {
  version: string = '';
  currentYear: number = new Date().getFullYear();

  constructor(private versionService: VersionService) {}

  ngOnInit(): void {
    this.versionService.getVersion().subscribe({
      next: (data) => {
        this.version = data.version;
      },
      error: (error) => {
        console.error('Error fetching version:', error);
        this.version = '1.0.0'; // Fallback version
      }
    });
  }
}

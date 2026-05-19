import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdminSignatureComponent } from './admin-signature.component';

describe('AdminSignatureComponent', () => {
  let component: AdminSignatureComponent;
  let fixture: ComponentFixture<AdminSignatureComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminSignatureComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(AdminSignatureComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

// Free-draw signature mechanics shared by signature pads that had identical copies
// (scaled pointer coordinates, stroke rendering). Components keep their own ViewChild,
// mode toggle, confirmation flag and save flow, and delegate only the drawing here.
export class CanvasSignaturePad {
  private ctx: CanvasRenderingContext2D | null = null;
  private canvas: HTMLCanvasElement | null = null;
  private drawing = false;
  private lastX = 0;
  private lastY = 0;

  attach(canvas: HTMLCanvasElement | undefined): void {
    if (!canvas) return;
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    if (this.ctx) {
      this.ctx.lineWidth = 2.5;
      this.ctx.lineCap = 'round';
      this.ctx.lineJoin = 'round';
      this.ctx.strokeStyle = '#0f766e';
    }
  }

  startDrawing(e: MouseEvent | TouchEvent): void {
    if (!this.ctx || !this.canvas) return;
    this.drawing = true;
    const { x, y } = this.getCoordinates(e);
    this.lastX = x;
    this.lastY = y;
  }

  // Returns true when a stroke segment was drawn, so the caller can mark itself confirmed.
  draw(e: MouseEvent | TouchEvent): boolean {
    if (!this.drawing || !this.ctx) return false;
    e.preventDefault();
    const { x, y } = this.getCoordinates(e);
    this.ctx.beginPath();
    this.ctx.moveTo(this.lastX, this.lastY);
    this.ctx.lineTo(x, y);
    this.ctx.stroke();
    this.lastX = x;
    this.lastY = y;
    return true;
  }

  stopDrawing(): void {
    this.drawing = false;
  }

  clear(): void {
    if (!this.canvas || !this.ctx) return;
    this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
  }

  private getCoordinates(e: MouseEvent | TouchEvent): { x: number; y: number } {
    const canvas = this.canvas!;
    const rect = canvas.getBoundingClientRect();
    const scaleX = canvas.width / rect.width;
    const scaleY = canvas.height / rect.height;
    if (window.TouchEvent && e instanceof TouchEvent) {
      return { x: (e.touches[0].clientX - rect.left) * scaleX, y: (e.touches[0].clientY - rect.top) * scaleY };
    }
    const m = e as MouseEvent;
    return { x: (m.clientX - rect.left) * scaleX, y: (m.clientY - rect.top) * scaleY };
  }
}

import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject, Subject } from 'rxjs';
import { environment } from '../../environments/environment';
import { UserComparison } from '../models/csv-sync.model';

export interface UploadProgress {
    message: string;
    percent: number;
}

export interface SyncProgressUpdate {
    processed: number;
    failed: number;
    skipped: number;
}

@Injectable({
    providedIn: 'root'
})
export class UserSyncSignalrService {
    private hubConnection: signalR.HubConnection | null = null;

    public uploadProgress$ = new Subject<UploadProgress>();
    public comparisonResult$ = new Subject<UserComparison>();
    public syncProgress$ = new Subject<SyncProgressUpdate>();
    public signatureUpdated$ = new Subject<void>();
    public connectionId$ = new BehaviorSubject<string | null>(null);

    constructor() { }

    public async startConnection(): Promise<void> {
        if (this.hubConnection && this.hubConnection.state === signalR.HubConnectionState.Connected) {
            return;
        }

        const baseUrl = environment.apiUrl.replace(/\/api\/?$/, '');
        this.hubConnection = new signalR.HubConnectionBuilder()
            .withUrl(baseUrl + '/hubs/sync')
            .withAutomaticReconnect()
            .build();

        this.registerHandlers();

        try {
            await this.hubConnection.start();
            console.log('SignalR Connection started', this.hubConnection.connectionId);
            this.connectionId$.next(this.hubConnection.connectionId ?? null);
        } catch (err) {
            console.error('Error while starting SignalR connection: ' + err);
        }
    }

    public stopConnection() {
        if (this.hubConnection) {
            this.hubConnection.stop();
            this.connectionId$.next(null);
        }
    }

    private registerHandlers() {
        if (!this.hubConnection) return;

        this.hubConnection.on('UploadProgress', (data: { message: string, percent: number }) => {
            this.uploadProgress$.next(data);
        });

        this.hubConnection.on('ComparisonResult', (comparison: UserComparison) => {
            this.comparisonResult$.next(comparison);
        });

        this.hubConnection.on('SyncProgress', (data: SyncProgressUpdate) => {
            this.syncProgress$.next(data);
        });

        this.hubConnection.on('SignatureUpdated', () => {
            this.signatureUpdated$.next();
        });
    }

    public getConnectionId(): string | null {
        return this.hubConnection?.connectionId ?? null;
    }
}

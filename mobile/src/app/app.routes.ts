import { Routes } from '@angular/router';
import { authGuard } from './core/http';
import { Shell } from './layout/shell';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/auth/login').then(m => m.LoginPage), title: 'Sign in' },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard').then(m => m.DashboardPage), title: 'Dashboard' },
      { path: 'discover', loadComponent: () => import('./features/discover/discover').then(m => m.DiscoverPage), title: 'Discover' },
      { path: 'discover/jobs/:id', loadComponent: () => import('./features/discover/job-detail').then(m => m.JobDetailPage), title: 'Discovery job' },
      { path: 'leads', loadComponent: () => import('./features/leads/lead-list').then(m => m.LeadListPage), title: 'Leads' },
      { path: 'leads/new', loadComponent: () => import('./features/leads/lead-form').then(m => m.LeadFormPage), title: 'New lead' },
      { path: 'leads/import', loadComponent: () => import('./features/leads/lead-import').then(m => m.LeadImportPage), title: 'Import CSV' },
      { path: 'leads/duplicates', loadComponent: () => import('./features/leads/duplicates').then(m => m.DuplicatesPage), title: 'Duplicates' },
      { path: 'leads/:id', loadComponent: () => import('./features/leads/lead-detail').then(m => m.LeadDetailPage), title: 'Lead' },
      { path: 'leads/:id/pitch', loadComponent: () => import('./features/pitch/pitch-studio').then(m => m.PitchStudioPage), title: 'Pitch Studio' },
      { path: 'approvals', loadComponent: () => import('./features/approvals/approval-list').then(m => m.ApprovalListPage), title: 'Approvals' },
      { path: 'approvals/:id', loadComponent: () => import('./features/approvals/approval-detail').then(m => m.ApprovalDetailPage), title: 'Review email' },
      { path: 'crm', loadComponent: () => import('./features/crm/pipeline').then(m => m.PipelinePage), title: 'CRM' },
      { path: 'followups', loadComponent: () => import('./features/followups/followups').then(m => m.FollowUpsPage), title: 'Follow-ups' },
      { path: 'notifications', loadComponent: () => import('./features/notifications/notifications').then(m => m.NotificationsPage), title: 'Notifications' },
      { path: 'reports', loadComponent: () => import('./features/reports/reports').then(m => m.ReportsPage), title: 'Reports' },
      { path: 'more', loadComponent: () => import('./features/settings/more').then(m => m.MorePage), title: 'More' },
      { path: 'settings', loadComponent: () => import('./features/settings/settings').then(m => m.SettingsPage), title: 'Settings' },
      { path: 'settings/integrations', loadComponent: () => import('./features/settings/integrations').then(m => m.IntegrationsPage), title: 'Integrations' },
      { path: 'settings/usage', loadComponent: () => import('./features/settings/usage').then(m => m.UsagePage), title: 'API usage' },
      { path: 'settings/compliance', loadComponent: () => import('./features/settings/compliance').then(m => m.CompliancePage), title: 'Compliance' },
    ],
  },
  { path: '**', redirectTo: '' },
];

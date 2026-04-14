import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminUserService } from '../../../services/admin-user.service';

@Component({
  selector: 'app-admin-user-list',
  standalone: true,
  imports: [FormsModule, DatePipe],
  templateUrl: './admin-user-list.component.html',
})
export class AdminUserListComponent implements OnInit {
  protected readonly userService = inject(AdminUserService);

  protected readonly search = signal('');

  protected readonly filteredUsers = computed(() => {
    const term = this.search().toLowerCase();
    const users = this.userService.users();
    if (!term) return users;
    return users.filter(
      u =>
        (u.email ?? '').toLowerCase().includes(term) ||
        (u.displayName ?? '').toLowerCase().includes(term),
    );
  });

  private debounceTimeout: ReturnType<typeof setTimeout> | null = null;

  async ngOnInit(): Promise<void> {
    await this.userService.loadUsers();
  }

  onSearchInput(value: string): void {
    this.search.set(value);
    if (this.debounceTimeout) clearTimeout(this.debounceTimeout);
    this.debounceTimeout = setTimeout(() => {
      this.userService.loadUsers(value || undefined);
    }, 300);
  }

  async onTierChange(userId: string, tier: string): Promise<void> {
    await this.userService.updateTier(userId, tier);
  }
}

import Testing
@testable import AmbystoUI

// APS-19: a click on the item that closes the popover must not reopen it in the same click.

@Suite("PopoverToggle")
struct PopoverToggleTests {
    @Test func opensWhenItNeverClosed() {
        #expect(PopoverToggle.shouldOpen(now: 100, lastClose: nil))
    }

    @Test func doesNotReopenRightAfterAClose() {
        #expect(!PopoverToggle.shouldOpen(now: 100.1, lastClose: 100))
        #expect(!PopoverToggle.shouldOpen(now: 100, lastClose: 100))
    }

    @Test func opensOnceTheGuardHasPassed() {
        #expect(PopoverToggle.shouldOpen(now: PopoverToggle.reopenGuard, lastClose: 0))   // exactly 0.3 s
        #expect(PopoverToggle.shouldOpen(now: 100.5, lastClose: 100))
        #expect(PopoverToggle.shouldOpen(now: 160, lastClose: 100))
    }

    @Test func opensWhenTheClockWentBackwards() {
        #expect(PopoverToggle.shouldOpen(now: 90, lastClose: 100))
    }
}

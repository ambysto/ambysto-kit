import Testing
@testable import AmbystoUI

// APS-16: the pixel "A" of the floating bar's header. (VoiceOver grouping has no unit test; the reviewer checks the diff.)

@MainActor
@Suite("Suite views")
struct SuiteViewsTests {
    @Test func brandMarkGridHasSeventeenLitAndOneDimCell() {
        #expect(BrandMark.grid.count == 7)
        #expect(BrandMark.grid.allSatisfy { $0.count == 5 })
        #expect(BrandMark.litCount == 17)
        #expect(BrandMark.dimCount == 1)
    }
}

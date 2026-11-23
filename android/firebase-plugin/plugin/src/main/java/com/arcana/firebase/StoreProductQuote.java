package com.arcana.firebase;

import com.android.billingclient.api.ProductDetails;

/** Selects the exact supported one-time or prepaid offer without starting a purchase. */
final class StoreProductQuote {
    final String price, token, period;
    private StoreProductQuote(String price, String token, String period) {
        this.price = price; this.token = token; this.period = period;
    }

    static StoreProductQuote find(ProductDetails item, String plan) {
        if (item.getProductType().equals("inapp")) {
            var offers = item.getOneTimePurchaseOfferDetailsList();
            if (offers != null) for (var offer : offers)
                if (offer.getRentalDetails() == null && offer.getPreorderDetails() == null)
                    return new StoreProductQuote(offer.getFormattedPrice(), offer.getOfferToken(), "");
        } else {
            var offers = item.getSubscriptionOfferDetails();
            if (offers != null) for (var offer : offers) {
                if (!plan.equals(offer.getBasePlanId()) || offer.getOfferId() != null) continue;
                var phases = offer.getPricingPhases().getPricingPhaseList(); if (phases.size() != 1) continue;
                var phase = phases.get(0);
                String expected = plan.equals("month-prepaid") ? "P1M" : plan.equals("six-months-prepaid") ? "P6M" : "";
                if (expected.isEmpty() || !expected.equals(phase.getBillingPeriod()) ||
                    phase.getRecurrenceMode() != ProductDetails.RecurrenceMode.NON_RECURRING) continue;
                return new StoreProductQuote(phase.getFormattedPrice(), offer.getOfferToken(), phase.getBillingPeriod());
            }
        }
        return null;
    }
}

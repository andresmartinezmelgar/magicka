package com.arcana.firebase;

import android.app.Activity;
import androidx.annotation.NonNull;
import com.android.billingclient.api.*;
import java.util.*;
import org.json.*;
import org.godotengine.godot.Godot;
import org.godotengine.godot.plugin.*;

/** Presenta precios y recibos de Play. El servidor entrega y confirma cada compra. */
public final class ArcanaBillingPlugin extends GodotPlugin {
    private Operation active;
    public ArcanaBillingPlugin(Godot godot) { super(godot); }
    @NonNull @Override public String getPluginName() { return "ArcanaBilling"; }
    @NonNull @Override public Set<SignalInfo> getPluginSignals() {
        return new HashSet<>(Arrays.asList(new SignalInfo("billing_result", String.class, String.class, String.class),
            new SignalInfo("billing_catalogue", String.class, String.class)));
    }
    @UsedByGodot public void beginPurchase(String id, String product, String owner, boolean restore) {
        beginStorePurchase(id, product, "inapp", "", owner, restore);
    }
    @UsedByGodot public void beginStorePurchase(String id, String product, String type, String plan, String owner, boolean restore) {
        runOnHostThread(() -> {
            if (active != null || getActivity() == null || !(type.equals("inapp") || type.equals("subs"))) {
                emitSignal("billing_result", id, "unavailable", ""); return;
            }
            Operation op = new Operation(id, product, type, plan, owner, restore, null); active = op;
            try { op.connect(getActivity()); } catch (RuntimeException e) { op.finish("unavailable", ""); }
        });
    }
    @UsedByGodot public void queryCatalogue(String id, String catalogue) {
        runOnHostThread(() -> {
            if (active != null || getActivity() == null) { emitSignal("billing_catalogue", id, "[]"); return; }
            try {
                JSONArray entries = new JSONArray(catalogue);
                if (entries.length() > 12) { emitSignal("billing_catalogue", id, "[]"); return; }
                Operation op = new Operation(id, "", "", "", "", false, entries); active = op; op.connect(getActivity());
            } catch (JSONException | RuntimeException e) {
                if (active != null && active.id.equals(id)) active.finish("unavailable", "");
                else emitSignal("billing_catalogue", id, "[]");
            }
        });
    }
    @UsedByGodot public void cancelPurchase(String id) {
        runOnHostThread(() -> { if (active != null && active.id.equals(id)) active.finish("closed", ""); });
    }
    @Override public void onMainDestroy() { if (active != null) active.finish("closed", ""); }

    private static final class Quote {
        final String price, token, period;
        Quote(String price, String token, String period) { this.price = price; this.token = token; this.period = period; }
    }
    private static Quote quote(ProductDetails item, String plan) {
        if (item.getProductType().equals("inapp")) {
            var offers = item.getOneTimePurchaseOfferDetailsList();
            if (offers != null) for (var offer : offers)
                if (offer.getRentalDetails() == null && offer.getPreorderDetails() == null)
                    return new Quote(offer.getFormattedPrice(), offer.getOfferToken(), "");
        } else {
            var offers = item.getSubscriptionOfferDetails();
            if (offers != null) for (var offer : offers) {
                if (!plan.equals(offer.getBasePlanId()) || offer.getOfferId() != null) continue;
                var phases = offer.getPricingPhases().getPricingPhaseList(); if (phases.size() != 1) continue;
                var phase = phases.get(0);
                String expected = plan.equals("month-prepaid") ? "P1M" : plan.equals("six-months-prepaid") ? "P6M" : "";
                if (expected.isEmpty() || !expected.equals(phase.getBillingPeriod()) ||
                    phase.getRecurrenceMode() != ProductDetails.RecurrenceMode.NON_RECURRING) continue;
                return new Quote(phase.getFormattedPrice(), offer.getOfferToken(), phase.getBillingPeriod());
            }
        }
        return null;
    }
    private final class Operation implements BillingClientStateListener, PurchasesUpdatedListener {
        final String id, product, type, plan, owner;
        final boolean restore;
        final JSONArray catalogue, prices = new JSONArray();
        BillingClient client;
        Operation(String id, String product, String type, String plan, String owner, boolean restore, JSONArray catalogue) {
            this.id = id; this.product = product; this.type = type; this.plan = plan; this.owner = owner;
            this.restore = restore; this.catalogue = catalogue;
        }
        void connect(Activity activity) {
            client = BillingClient.newBuilder(activity).setListener(this).enableAutoServiceReconnection()
                .enablePendingPurchases(PendingPurchasesParams.newBuilder().enableOneTimeProducts().enablePrepaidPlans().build()).build();
            client.startConnection(this);
        }
        void finish(String status, String token) {
            if (active != this) return;
            active = null; if (client != null) client.endConnection();
            if (catalogue != null) emitSignal("billing_catalogue", id, prices.toString());
            else emitSignal("billing_result", id, status, token);
        }
        @Override public void onBillingServiceDisconnected() { runOnHostThread(() -> finish("unavailable", "")); }
        @Override public void onBillingSetupFinished(@NonNull BillingResult result) {
            runOnHostThread(() -> {
                if (active != this) return;
                if (result.getResponseCode() != BillingClient.BillingResponseCode.OK) { finish("unavailable", ""); return; }
                if (catalogue != null) catalogue("inapp"); else owned(!restore);
            });
        }
        void catalogue(String productType) {
            Set<String> unique = new HashSet<>(); var query = new ArrayList<QueryProductDetailsParams.Product>();
            for (int i = 0; i < catalogue.length(); i++) {
                var entry = catalogue.optJSONObject(i);
                if (entry == null || !productType.equals(entry.optString("productType"))) continue;
                String productId = entry.optString("productId");
                if (!productId.isEmpty() && unique.add(productId)) query.add(QueryProductDetailsParams.Product.newBuilder()
                    .setProductId(productId).setProductType(productType).build());
            }
            if (query.isEmpty()) { nextCatalogue(productType); return; }
            client.queryProductDetailsAsync(QueryProductDetailsParams.newBuilder().setProductList(query).build(),
                (result, details) -> runOnHostThread(() -> {
                    if (active != this) return;
                    if (result.getResponseCode() == BillingClient.BillingResponseCode.OK) try {
                        for (var item : details.getProductDetailsList()) for (int i = 0; i < catalogue.length(); i++) {
                            var entry = catalogue.getJSONObject(i);
                            if (!productType.equals(entry.optString("productType")) || !item.getProductId().equals(entry.optString("productId"))) continue;
                            var value = quote(item, entry.optString("basePlanId"));
                            if (value != null) prices.put(new JSONObject().put("key", entry.getString("key"))
                                .put("price", value.price).put("period", value.period));
                        }
                    } catch (JSONException e) { finish("unavailable", ""); return; }
                    nextCatalogue(productType);
                }));
        }
        void nextCatalogue(String type) { if (type.equals("inapp")) catalogue("subs"); else finish("catalogue", ""); }
        boolean select(List<Purchase> purchases) {
            if (purchases == null) return false;
            for (var p : purchases) {
                if (!p.getProducts().contains(product)) continue;
                var account = p.getAccountIdentifiers();
                if (account == null || !owner.equals(account.getObfuscatedAccountId())) { finish("other_account", ""); return true; }
                if (p.getPurchaseState() == Purchase.PurchaseState.PURCHASED) finish("purchased", p.getPurchaseToken());
                else finish("pending", "");
                return true;
            }
            return false;
        }
        void owned(boolean launchIfMissing) {
            client.queryPurchasesAsync(QueryPurchasesParams.newBuilder().setProductType(type).build(), (result, purchases) -> runOnHostThread(() -> {
                if (active != this) return;
                if (result.getResponseCode() != BillingClient.BillingResponseCode.OK) { finish("unavailable", ""); return; }
                if (select(purchases)) return;
                if (launchIfMissing) checkout(); else finish("none", "");
            }));
        }
        void checkout() {
            var query = QueryProductDetailsParams.Product.newBuilder().setProductId(product).setProductType(type).build();
            client.queryProductDetailsAsync(QueryProductDetailsParams.newBuilder().setProductList(Collections.singletonList(query)).build(),
                (result, details) -> runOnHostThread(() -> {
                    if (active != this) return;
                    if (result.getResponseCode() != BillingClient.BillingResponseCode.OK || getActivity() == null) { finish("unavailable", ""); return; }
                    for (var item : details.getProductDetailsList()) {
                        if (!product.equals(item.getProductId())) continue;
                        var value = quote(item, plan); if (value == null) continue;
                        var entry = BillingFlowParams.ProductDetailsParams.newBuilder().setProductDetails(item).setOfferToken(value.token).build();
                        var started = client.launchBillingFlow(getActivity(), BillingFlowParams.newBuilder()
                            .setObfuscatedAccountId(owner).setProductDetailsParamsList(Collections.singletonList(entry)).build());
                        if (started.getResponseCode() == BillingClient.BillingResponseCode.ITEM_ALREADY_OWNED) owned(false);
                        else if (started.getResponseCode() != BillingClient.BillingResponseCode.OK) finish("unavailable", "");
                        return;
                    }
                    finish("unavailable", "");
                }));
        }
        @Override public void onPurchasesUpdated(@NonNull BillingResult result, List<Purchase> purchases) {
            runOnHostThread(() -> {
                if (active != this) return;
                switch (result.getResponseCode()) {
                    case BillingClient.BillingResponseCode.OK: if (!select(purchases)) finish("none", ""); break;
                    case BillingClient.BillingResponseCode.ITEM_ALREADY_OWNED: owned(false); break;
                    case BillingClient.BillingResponseCode.USER_CANCELED: finish("closed", ""); break;
                    default: finish("unavailable", "");
                }
            });
        }
    }
}
